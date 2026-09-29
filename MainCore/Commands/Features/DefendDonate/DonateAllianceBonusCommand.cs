namespace MainCore.Commands.Features.DefendDonate
{
    // Donates up to (Wood, Clay, Iron, Crop) to the alliance's Recruitment (Faster troop
    // production) bonus via /alliance/bonuses. Added 2026-09-27 for DefendDonateTask - always
    // the Recruitment bonus, per explicit user choice.
    //
    // This page is JS/AJAX-driven, not a plain <form> post (see AllianceBonusParser): filling
    // an input has to actually fire its onkeyup handler for the page's own JS to re-validate
    // the amount and un-disable the Contribute button. ASSUMPTION, not yet verified against a
    // live donation attempt: browser.Input is assumed to type via real simulated keystrokes
    // (same as every other text input filled elsewhere in this codebase), which fires genuine
    // keyup events in the browser and should trigger checkAndChange the same way a human
    // typing would - re-check this if the Contribute button ever stays disabled in practice.
    [Handler]
    public static partial class DonateAllianceBonusCommand
    {
        public sealed record Command(VillageId VillageId, long Wood, long Clay, long Iron, long Crop) : IVillageCommand;

        // Returns the total actually submitted (0 if there was nothing donatable, e.g. the
        // account's daily contribution limit was already used up).
        private static async ValueTask<Result<long>> HandleAsync(
            Command command,
            IChromeBrowser browser,
            ToAllianceBonusPageCommand.Handler toAllianceBonusPageCommand,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            var toPageResult = await toAllianceBonusPageCommand.HandleAsync(new(), cancellationToken);
            if (toPageResult.IsFailed) return Result.Fail(toPageResult.Errors);

            var dailyRemaining = AllianceBonusParser.GetDailyLimitRemaining(browser.Html);
            if (dailyRemaining <= 0)
            {
                logger.Information("Alliance daily contribution limit already reached in {VillageId}, skipping donation.", command.VillageId);
                return 0L;
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

                var maxDonatable = AllianceBonusParser.GetMaxDonatable(browser.Html, index) ?? 0;
                var capped = Math.Min(amount, maxDonatable);

                // Greedily spend the remaining daily budget resource-by-resource (Wood, Clay,
                // Iron, Crop order) rather than splitting it proportionally - simplest
                // behaviour that still never exceeds the account-wide cap.
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

            var (_, isFailed, radio, errors) = await browser.GetElement(AllianceBonusParser.GetRecruitmentBonusRadio, cancellationToken);
            if (isFailed) return Result.Fail(errors).WithError("Failed to find the Recruitment bonus radio button");

            Result result;
            result = await browser.Click(radio, cancellationToken);
            if (result.IsFailed) return Result.Fail(result.Errors);

            foreach (var (index, amount) in toDonate)
            {
                var resourceIndex = index;
                (_, isFailed, var input, errors) = await browser.GetElement(doc => AllianceBonusParser.GetResourceInput(doc, resourceIndex), cancellationToken);
                if (isFailed) return Result.Fail(errors).WithError($"Failed to find donation input for resource #{resourceIndex}");

                result = await browser.Input(input, $"{amount}", cancellationToken);
                if (result.IsFailed) return Result.Fail(result.Errors);
            }

            (_, isFailed, var contributeButton, errors) = await browser.GetElement(AllianceBonusParser.GetContributeButton, cancellationToken);
            if (isFailed) return Result.Fail(errors).WithError("Failed to find the Contribute button");

            result = await browser.Click(contributeButton, cancellationToken);
            if (result.IsFailed) return Result.Fail(result.Errors);

            logger.Information("Donated {Total} total resources to the alliance's Recruitment bonus from {VillageId}.", runningTotal, command.VillageId);
            return Result.Ok(runningTotal);
        }
    }
}
