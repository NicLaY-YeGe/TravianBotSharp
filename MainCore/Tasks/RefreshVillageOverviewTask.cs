using MainCore.Commands.Features.DodgeTroop;
using MainCore.Commands.Features.SendResource;
using MainCore.Commands.Features.TrainTroop;
using MainCore.Commands.Features.VillageOverview;
using MainCore.Tasks.Base;

namespace MainCore.Tasks
{
    // 2026-11-11 (village overview tab, user request): the "Read from game" button of the tab queues this task. It walks
    // through the villages of the account and remembers what each one shows right now (VillageOverviewStore):
    //   1. Rally Point > Overview: troops at home, reinforcements of the player's other villages, troops on their way
    //      out / back and stationed elsewhere. The page lists only 10 movements per section; when the heading says there
    //      are more, the "Show all" link of that section is followed (otherwise the number is flagged as incomplete);
    //   2. Marketplace: free / total merchants;
    //   3. Barracks, Stable, Workshop (only those the village has): the end of the training queue.
    // Resources, Smithy and Celebration are not read here - they come from the database and the village settings.
    //
    // Lifecycle: one-shot, spawned on demand like DodgeTroopTask (NOT bootstrapped in RxQueue by design). The task ends
    // with ExecuteAt unchanged, so TimerManager removes it (CLAUDE.md rule 16 - intended here); Restart drops it.
    //
    // Secondary task (CLAUDE.md rule 4): it never returns Retry or Stop. Every village has a time budget of its own and a
    // failure only skips the rest of THAT village (Warning); a user pause stays a pause and a dead browser is the
    // behaviors' business.
    [Handler]
    public static partial class RefreshVillageOverviewTask
    {
        public sealed class Task : AccountTask
        {
            public Task(AccountId accountId) : base(accountId)
            {
            }

            protected override string TaskName => "Read village overview";
        }

        private const int VillageBudgetSeconds = 100;
        private const int OverviewWaitSeconds = 8;
        private const int ShowAllWaitSeconds = 10;
        private const int ElementWaitSeconds = 6;

        private static readonly BuildingEnums[] TrainingBuildings = [BuildingEnums.Barracks, BuildingEnums.Stable, BuildingEnums.Workshop];

        private static async ValueTask<Result> HandleAsync(
            Task task,
            IChromeBrowser browser,
            SwitchVillageCommand.Handler switchVillageCommand,
            ToRallyPointOverviewCommand.Handler toRallyPointOverviewCommand,
            ToSendResourcePageCommand.Handler toSendResourcePageCommand,
            ToTrainTroopPageCommand.Handler toTrainTroopPageCommand,
            AppDbContext context,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            var villages = context.Villages
                .AsNoTracking()
                .Where(x => x.AccountId == task.AccountId.Value)
                .OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.Name })
                .ToList();

            var read = 0;
            foreach (var village in villages)
            {
                if (cancellationToken.IsCancellationRequested) return Cancel.Error;

                using var villageCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                villageCts.CancelAfter(TimeSpan.FromSeconds(VillageBudgetSeconds));

                var present = context.Buildings
                    .AsNoTracking()
                    .Where(x => x.VillageId == village.Id && x.Level > 0)
                    .Select(x => x.Type)
                    .ToList()
                    .Where(x => TrainingBuildings.Contains(x))
                    .Distinct()
                    .ToList();

                var result = await ReadVillageAsync(
                    new VillageId(village.Id), village.Name, present, browser,
                    switchVillageCommand, toRallyPointOverviewCommand, toSendResourcePageCommand, toTrainTroopPageCommand,
                    logger, villageCts.Token);

                // a user pause stays a pause
                if (cancellationToken.IsCancellationRequested) return Cancel.Error;
                if (result.IsSuccess)
                {
                    read++;
                    continue;
                }

                if (result.HasError<BrowserClosed>()) return result;

                var reason = villageCts.IsCancellationRequested
                    ? $"no answer within {VillageBudgetSeconds}s"
                    : string.Join(" | ", result.Reasons.Select(x => x.Message).Where(x => !string.IsNullOrWhiteSpace(x)));
                logger.Warning("Village overview: {Village} was not read completely ({Reason}) - going on with the next village.", village.Name, reason);
            }

            logger.Information("Village overview: read {Read} of {Total} village(s).", read, villages.Count);
            return Result.Ok();
        }

        // Ok = every step that applies to the village worked; a failed step only skips what is left of this village.
        private static async Task<Result> ReadVillageAsync(
            VillageId villageId,
            string villageName,
            List<BuildingEnums> trainingBuildings,
            IChromeBrowser browser,
            SwitchVillageCommand.Handler switchVillageCommand,
            ToRallyPointOverviewCommand.Handler toRallyPointOverviewCommand,
            ToSendResourcePageCommand.Handler toSendResourcePageCommand,
            ToTrainTroopPageCommand.Handler toTrainTroopPageCommand,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            var result = await switchVillageCommand.HandleAsync(new(villageId), cancellationToken);
            if (result.IsFailed) return result;

            var notes = new List<string>();

            // 1. troops
            var troops = await ReadTroopsAsync(villageId, browser, toRallyPointOverviewCommand, cancellationToken);
            if (troops.IsFailed)
            {
                if (IsFatal(troops, cancellationToken)) return troops;
                if (troops.HasError<MissingBuilding>()) logger.Information("Village overview: {Village} has no Rally Point - troops skipped.", villageName);
                else notes.Add("troops: " + FirstMessage(troops));
            }

            // 2. merchants
            var merchants = await toSendResourcePageCommand.HandleAsync(new(villageId), cancellationToken);
            if (merchants.IsFailed)
            {
                if (IsFatal(merchants, cancellationToken)) return merchants;
                if (merchants.HasError<MissingBuilding>()) logger.Information("Village overview: {Village} has no Marketplace - merchants skipped.", villageName);
                else notes.Add("merchants: " + FirstMessage(merchants));
            }
            else
            {
                var counts = SendResourceParser.GetMerchantCounts(browser.Html);
                if (counts is null) notes.Add("merchants: the merchant line was not on the page");
                else VillageOverviewStore.SetMerchants(villageId.Value, counts.Value.Free, counts.Value.Total);
            }

            // 3. training queues
            foreach (var building in trainingBuildings)
            {
                var page = await toTrainTroopPageCommand.HandleAsync(new(villageId, building), cancellationToken);
                if (page.IsFailed)
                {
                    if (IsFatal(page, cancellationToken)) return page;
                    if (!page.HasError<MissingBuilding>()) notes.Add($"{building}: " + FirstMessage(page));
                    continue;
                }

                // no queue table = nothing is being trained
                var seconds = TrainQueueParser.GetSecondsRemaining(browser.Html) ?? 0;
                VillageOverviewStore.SetTrainQueue(villageId.Value, (int)building, seconds);
            }

            if (notes.Count > 0) return Skip.Error.WithError(string.Join("; ", notes));
            return Result.Ok();
        }

        private static async Task<Result> ReadTroopsAsync(
            VillageId villageId,
            IChromeBrowser browser,
            ToRallyPointOverviewCommand.Handler toRallyPointOverviewCommand,
            CancellationToken cancellationToken)
        {
            var result = await toRallyPointOverviewCommand.HandleAsync(new(villageId), cancellationToken);
            if (result.IsFailed) return result;

            // the tab content is rendered after the tab switch
            var shown = await WaitAsync(browser, OverviewWaitSeconds, doc => RallyPointTroopsParser.Get(doc) is not null, cancellationToken);
            if (!shown) return Skip.Error.WithError("the Rally Point overview did not show up");

            var troops = RallyPointTroopsParser.Get(browser.Html)!;
            var returning = troops.Returning;
            var moving = troops.Moving;
            var complete = true;

            // The page lists 10 movements per section; "Show all" (filter=1 incoming, filter=2 outgoing) lists every one.
            if (troops.IncomingTruncated)
            {
                var all = await ShowAllAsync(browser, 1, cancellationToken);
                if (all is { IncomingTruncated: false }) returning = all.Returning;
                else complete = false;
            }

            if (troops.OutgoingTruncated)
            {
                var all = await ShowAllAsync(browser, 2, cancellationToken);
                if (all is { OutgoingTruncated: false }) moving = all.Moving;
                else complete = false;
            }

            VillageOverviewStore.SetTroops(villageId.Value, new TroopSnapshot(
                DateTime.Now, troops.Names, troops.Home, troops.Reinforcement, moving, returning, troops.Stationed, complete));
            return Result.Ok();
        }

        // Follows the "Show all" link of one section; null when the link is not there / does not work.
        private static async Task<RallyPointTroops?> ShowAllAsync(IChromeBrowser browser, int filter, CancellationToken cancellationToken)
        {
            var element = await FindElementAsync(browser, By.CssSelector($"a.openedClosedSwitch[href*=\"filter={filter}\"]"), ElementWaitSeconds, cancellationToken);
            if (element is null) return null;

            var click = await browser.Click(element, cancellationToken);
            if (click.IsFailed) return null;

            var shown = await WaitAsync(browser, ShowAllWaitSeconds, doc =>
            {
                var parsed = RallyPointTroopsParser.Get(doc);
                if (parsed is null) return false;
                return filter == 1 ? !parsed.IncomingTruncated : !parsed.OutgoingTruncated;
            }, cancellationToken);

            return shown ? RallyPointTroopsParser.Get(browser.Html) : null;
        }

        private static bool IsFatal(Result result, CancellationToken cancellationToken)
            => result.HasError<BrowserClosed>() || cancellationToken.IsCancellationRequested;

        private static string FirstMessage(Result result)
            => result.Errors.Select(x => x.Message).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "failed";

        // Polls for the element without the browser-wide 3 minute wait (CLAUDE.md rule 9).
        private static async Task<IWebElement?> FindElementAsync(IChromeBrowser browser, By by, int seconds, CancellationToken cancellationToken)
        {
            var driver = browser.Driver;
            if (driver is null) return null;

            var deadline = DateTime.UtcNow.AddSeconds(seconds);
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var elements = driver.FindElements(by);
                    if (elements.Count > 0 && elements[0].Displayed) return elements[0];
                }
                catch (BrowserException)
                {
                    // page re-rendered between lookup and check - look again
                }

                if (DateTime.UtcNow >= deadline) return null;
                await System.Threading.Tasks.Task.Delay(250, cancellationToken);
            }
        }

        // Waits for a page condition under a limit of its own. false = it did not happen in time (or the village budget ran out).
        private static async Task<bool> WaitAsync(IChromeBrowser browser, int seconds, Func<HtmlDocument, bool> condition, CancellationToken cancellationToken)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(seconds));
            var result = await browser.Wait(driver =>
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(driver.PageSource);
                return condition(doc);
            }, cts.Token);
            return result.IsSuccess;
        }
    }
}
