namespace MainCore.Commands.Features.DefendDonate
{
    // Donates up to (Wood, Clay, Iron, Crop) from the CURRENTLY ACTIVE village to the alliance's
    // Recruitment (Faster troop production) bonus via /alliance/bonuses. Always the Recruitment
    // bonus, per explicit user choice (re-confirmed 2026-10-22).
    //
    // Reworked 2026-10-22 against two real page captures. The page is JS-driven: the Contribute
    // button only loses its "disabled" class once a bonus is selected AND a valid amount is typed,
    // and clicking it while "disabled" silently does nothing (see AllianceBonusParser). So the
    // sequence is verified step by step instead of trusted:
    //   1. click the bonus LABEL (the radio input itself may be hidden - prime suspect for the
    //      2026-09-27 180 s hang),
    //   2. type the amounts (real key events fire the page's onkeyup validation),
    //   3. wait (max 10 s) for the Contribute button to become enabled - proof of 1 + 2,
    //   4. click it, then wait (max 8 s) for a sign of success: the visible daily counter going up
    //      (confirmed from a real donation), the hidden donatedToday, or a response text.
    // If the success sign never shows, the donation is reported as UNCONFIRMED (warning + page dump),
    // not as failed - no real capture of a finished donation exists yet.
    // Everything is bounded by Command.TimeoutSeconds; any failure saves the page (PageDump).
    [Handler]
    public static partial class DonateAllianceBonusCommand
    {
        public sealed record Command(
            VillageId VillageId,
            long Wood,
            long Clay,
            long Iron,
            long Crop,
            int? TimeoutSeconds = null) : IVillageCommand;

        private static readonly TimeSpan ButtonEnableTimeout = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan ConfirmTimeout = TimeSpan.FromSeconds(8);

        // Returns the total submitted (0 if nothing was donatable, e.g. the daily limit is used up).
        private static async ValueTask<Result<long>> HandleAsync(
            Command command,
            IChromeBrowser browser,
            ToAllianceBonusPageCommand.Handler toAllianceBonusPageCommand,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (command.TimeoutSeconds is > 0) deadline.CancelAfter(TimeSpan.FromSeconds(command.TimeoutSeconds.Value));

            var result = await DonateAsync(command, browser, toAllianceBonusPageCommand, logger, deadline.Token);
            if (result.IsSuccess) return result;

            // A user pause (or the caller's own budget) is not a failure to diagnose.
            if (cancellationToken.IsCancellationRequested) return result;

            await PageDump.SaveAsync(browser, logger, "alliance-bonus-failed");

            if (deadline.IsCancellationRequested)
            {
                return Skip.Error.WithError(
                    $"Village {command.VillageId}: the alliance donation did not finish within {command.TimeoutSeconds}s, abandoned.");
            }

            return result;
        }

        private static async ValueTask<Result<long>> DonateAsync(
            Command command,
            IChromeBrowser browser,
            ToAllianceBonusPageCommand.Handler toAllianceBonusPageCommand,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            var toPageResult = await toAllianceBonusPageCommand.HandleAsync(new(), cancellationToken);
            if (toPageResult.IsFailed) return Result.Fail(toPageResult.Errors);

            if (AllianceBonusParser.IsDailyLimitReached(browser.Html))
            {
                logger.Information("Alliance daily contribution limit already reached, nothing donated from {VillageId} (the resources stay in the village).", command.VillageId);
                return 0L;
            }

            var dailyRemaining = AllianceBonusParser.GetDailyLimitRemaining(browser.Html);
            if (dailyRemaining <= 0)
            {
                logger.Information("No daily contribution budget left, nothing donated from {VillageId} (the resources stay in the village).", command.VillageId);
                return 0L;
            }

            // The Recruitment bonus must be on the page at all (not an alliance member, bonus not unlocked...).
            By bonusSelector;
            if (AllianceBonusParser.GetRecruitmentBonusLabel(browser.Html) is not null)
            {
                bonusSelector = By.CssSelector("label[for=\"bonusTroopProductionSpeed\"]");
            }
            else if (AllianceBonusParser.GetRecruitmentBonusRadio(browser.Html) is not null)
            {
                bonusSelector = By.Id("bonusTroopProductionSpeed");
            }
            else
            {
                return Skip.Error.WithError($"Village {command.VillageId}: the Recruitment bonus is not on the alliance bonus page.");
            }

            var requested = new Dictionary<int, long>
            {
                [1] = command.Wood,
                [2] = command.Clay,
                [3] = command.Iron,
                [4] = command.Crop,
            };

            var toDonate = new Dictionary<int, long>();
            var runningTotal = 0L;
            foreach (var (index, amount) in requested)
            {
                if (amount <= 0) continue;

                // The page's own per-resource maximum is the active village's current stock.
                var maxDonatable = AllianceBonusParser.GetMaxDonatable(browser.Html, index) ?? 0;
                var capped = Math.Min(amount, maxDonatable);

                // Spend the remaining daily budget resource by resource (Wood, Clay, Iron, Crop).
                var remainingBudget = dailyRemaining - runningTotal;
                if (remainingBudget <= 0) break;
                capped = Math.Min(capped, remainingBudget);

                if (capped <= 0) continue;
                toDonate[index] = capped;
                runningTotal += capped;
            }

            if (toDonate.Count == 0)
            {
                logger.Information("Nothing donatable to the alliance from {VillageId} right now.", command.VillageId);
                return Result.Ok(0L);
            }

            var donatedBefore = Math.Max(AllianceBonusParser.GetDonatedToday(browser.Html), AllianceBonusParser.GetDonatedTodayShown(browser.Html));

            // 1) select the bonus
            var (_, isFailed, bonusElement, errors) = await browser.GetElement(bonusSelector, cancellationToken);
            if (isFailed) return Result.Fail(errors).WithError("Failed to find the Recruitment bonus selector");

            var result = await browser.Click(bonusElement, cancellationToken);
            if (result.IsFailed) return Result.Fail(result.Errors);

            // 2) type the amounts
            foreach (var (index, amount) in toDonate)
            {
                var inputId = AllianceBonusParser.ResourceInputIds[index];
                (_, isFailed, var input, errors) = await browser.GetElement(By.Id(inputId), cancellationToken);
                if (isFailed) return Result.Fail(errors).WithError($"Failed to find donation input {inputId}");

                result = await browser.Input(input, $"{amount}", cancellationToken);
                if (result.IsFailed) return Result.Fail(result.Errors);
            }

            // 3) the button must have become clickable
            var enabledResult = await WaitBounded(browser, ButtonEnableTimeout, cancellationToken,
                doc => AllianceBonusParser.IsContributeEnabled(doc));
            if (enabledResult.IsFailed)
            {
                if (cancellationToken.IsCancellationRequested) return Result.Fail(enabledResult.Errors);
                return Skip.Error.WithError(
                    $"Village {command.VillageId}: the Contribute button stayed disabled after selecting the bonus and typing the amounts, nothing donated.");
            }

            // The page's own running total should equal what was typed (the game clamps to stock and to
            // the daily limit, so a difference is logged, not fatal).
            var pageSum = AllianceBonusParser.GetDonateSum(browser.Html);
            if (pageSum != runningTotal)
            {
                logger.Warning("Alliance donation from {VillageId}: the page shows a sum of {PageSum}, {Planned} was typed.",
                    command.VillageId, pageSum, runningTotal);
            }

            // 4) contribute, then look for a sign that it went through
            (_, isFailed, var contributeButton, errors) = await browser.GetElement(By.Id("donate_green"), cancellationToken);
            if (isFailed) return Result.Fail(errors).WithError("Failed to find the Contribute button");

            result = await browser.Click(contributeButton, cancellationToken);
            if (result.IsFailed) return Result.Fail(result.Errors);

            var confirmResult = await WaitBounded(browser, ConfirmTimeout, cancellationToken,
                doc => AllianceBonusParser.GetDonatedToday(doc) > donatedBefore
                    || AllianceBonusParser.GetDonatedTodayShown(doc) > donatedBefore
                    || AllianceBonusParser.GetDonationResponseText(doc) is not null);
            if (confirmResult.IsSuccess)
            {
                logger.Information("Donated {Total} resources ({Details}) to the alliance's Recruitment bonus from {VillageId}; daily total now {Today}.",
                    runningTotal,
                    string.Join(", ", toDonate.Select(x => $"{AllianceBonusParser.ResourceInputIds[x.Key]}={x.Value}")),
                    command.VillageId,
                    Math.Max(AllianceBonusParser.GetDonatedToday(browser.Html), AllianceBonusParser.GetDonatedTodayShown(browser.Html)));
                return Result.Ok(runningTotal);
            }

            if (cancellationToken.IsCancellationRequested) return Result.Fail(confirmResult.Errors);

            logger.Warning("Clicked Contribute for {Total} resources from {VillageId} but saw no confirmation (daily counter unchanged, no response text). Treating it as probably done; the page is saved for checking.",
                runningTotal, command.VillageId);
            await PageDump.SaveAsync(browser, logger, "alliance-bonus-unconfirmed");
            return Result.Ok(runningTotal);
        }

        // browser.Wait polls for up to 3 minutes; this caps it with its own, shorter limit while still
        // honouring the outer cancellation (user pause / overall deadline).
        private static async Task<Result> WaitBounded(
            IChromeBrowser browser,
            TimeSpan limit,
            CancellationToken cancellationToken,
            Func<HtmlDocument, bool> condition)
        {
            using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            bounded.CancelAfter(limit);

            return await browser.Wait(driver =>
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(driver.PageSource);
                return condition(doc);
            }, bounded.Token);
        }
    }
}
