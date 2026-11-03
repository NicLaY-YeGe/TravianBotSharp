using MainCore.Commands.Features.Scheduling;
using MainCore.Commands.Features.SmithyUpgrade;
using MainCore.Commands.UI.Misc;
using MainCore.Tasks.Base;

namespace MainCore.Tasks
{
    [Handler]
    public static partial class SmithyUpgradeTask
    {
        public sealed class Task : VillageTask
        {
            public Task(AccountId accountId, VillageId villageId) : base(accountId, villageId)
            {
            }

            protected override string TaskName => "Smithy upgrade";

            public override bool CanStart(AppDbContext context)
            {
                var enabled = context.BooleanByName(VillageId, VillageSettingEnums.SmithyUpgradeEnable);
                if (!enabled) return false;

                var busyUntil = context.ByName(VillageId, VillageSettingEnums.SmithyUpgradeBusyUntilUnixTime);
                if (busyUntil <= 0) return true;

                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                return now >= busyUntil;
            }
        }

        private static async ValueTask<Result> HandleAsync(
            Task task,
            IChromeBrowser browser,
            ToSmithyPageCommand.Handler toSmithyPageCommand,
            SmithyUpgradeCommand.Handler smithyUpgradeCommand,
            SaveVillageSettingCommand.Handler saveVillageSettingCommand,
            AppDbContext context,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            var troopSlot = context.ByName(task.VillageId, VillageSettingEnums.SmithyUpgradeTroopSlot);
            if (troopSlot <= 0) troopSlot = 1;

            // NOTE: AccountsInfo.Tribe is NOT a reliable source here - UpdateAccountInfoCommand
            // hard-codes it to TribeEnums.Any and never actually parses it from the page, so it's
            // effectively always "Any" (see CHANGELOG.md, 2026-08-09 - this caused the tribe-aware
            // GetResearchBlock fix to still fail with the same "research block not found" error,
            // since RallyPointTroopSlots.GetSlots(Any) returns an empty list). The account's real
            // tribe lives in the AccountSettingEnums.Tribe key-value setting instead - the same
            // one set via the Tribe selector on the Account Setting tab.
            var tribe = (TribeEnums)context.ByName(task.AccountId, AccountSettingEnums.Tribe);

            var pageResult = await toSmithyPageCommand.HandleAsync(new(task.VillageId), cancellationToken);
            if (pageResult.IsFailed)
            {
                if (pageResult.HasError<MissingBuilding>())
                {
                    var settings = new Dictionary<VillageSettingEnums, int>() {
                        { VillageSettingEnums.SmithyUpgradeEnable, 0 }
                    };
                    await saveVillageSettingCommand.HandleAsync(new(task.AccountId, task.VillageId, settings), cancellationToken);
                    logger.Warning("No smithy in this village, disabling smithy upgrade.");
                    return Skip.Error.WithErrors(pageResult.Errors);
                }
                return Stop.Error.WithErrors(pageResult.Errors);
            }

            // If a research is already running (for this or any other troop type in this
            // smithy - only one can run at a time), remember when it finishes so we don't
            // bother revisiting the Smithy until then.
            var secondsRemaining = SmithyParser.GetOngoingResearchSecondsRemaining(browser.Html);
            if (secondsRemaining is not null)
            {
                // 2026-10-25: the exact end plus a 30-60 s buffer (QueueDeferRules), so the next visit
                // never lands a moment before the research is really finished.
                var waitSeconds = QueueDeferRules.BusySeconds(secondsRemaining, Random.Shared);
                await SetBusyUntil(task, saveVillageSettingCommand, waitSeconds, cancellationToken);
                logger.Information("Smithy research already running in {VillageId}, {Seconds}s left - won't check again for {Wait}s (its end plus a small buffer).", task.VillageId, secondsRemaining.Value, waitSeconds);
                return Result.Ok();
            }

            var result = await smithyUpgradeCommand.HandleAsync(new(task.VillageId, troopSlot, tribe), cancellationToken);
            if (result.IsFailed) return Stop.Error.WithErrors(result.Errors);

            // Whether we just started a research or the page said it isn't available, never come back
            // to this page before the game says something can have changed (repeatedly hammering the
            // same page is the kind of pattern that looks bad even when each visit is harmless):
            //   - a research is running now (just started): wait for its exact end + buffer;
            //   - nothing readable running and no Improve button (resources short, level too low,
            //     already maxed...): the reason is unknown, so 45-60 minutes (random). It used to be a
            //     fixed 20 minutes, which showed as a visit every 20-22 minutes per village.
            var runningAfter = SmithyParser.GetOngoingResearchSecondsRemaining(browser.Html);
            var nextWaitSeconds = QueueDeferRules.BusySeconds(runningAfter, Random.Shared);
            await SetBusyUntil(task, saveVillageSettingCommand, nextWaitSeconds, cancellationToken);

            if (runningAfter is null)
            {
                logger.Information(
                    "Smithy in {VillageId}: nothing running and no Improve button (queue table on the page: {HasTable}) - next visit in {Minutes} min.",
                    task.VillageId, TrainQueueParser.HasQueueTable(browser.Html), nextWaitSeconds / 60);
            }
            else
            {
                logger.Information("Smithy research in {VillageId} ends in {Seconds}s - next visit in {Wait}s.", task.VillageId, runningAfter.Value, nextWaitSeconds);
            }

            return Result.Ok();
        }

        private static async System.Threading.Tasks.Task SetBusyUntil(Task task, SaveVillageSettingCommand.Handler saveVillageSettingCommand, int secondsFromNow, CancellationToken cancellationToken)
        {
            var busyUntil = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + secondsFromNow;
            var settings = new Dictionary<VillageSettingEnums, int>() {
                { VillageSettingEnums.SmithyUpgradeBusyUntilUnixTime, (int)busyUntil }
            };
            await saveVillageSettingCommand.HandleAsync(new(task.AccountId, task.VillageId, settings), cancellationToken);
        }
    }
}
