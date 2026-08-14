using MainCore.Models;

namespace MainCore.Commands.Features.DodgeTroop
{
    // Evolved 2026-08-13: cancels an outgoing ATTACK movement by target coordinate, using
    // RallyPointOverviewParser.GetOutgoingAttackAbortButton's headline-text match (see that
    // parser for why the previous <th class="coords"> approach was wrong). Replaces the old
    // village-name-based GetRecallButton usage, which only worked for reinforcements sent to
    // one of our own named villages.
    [Handler]
    public static partial class RecallTroopCommand
    {
        public sealed record Command(VillageId VillageId, int TargetX, int TargetY) : IVillageCommand;

        private static async ValueTask<Result> HandleAsync(
            Command command,
            IChromeBrowser browser,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            var (_, targetX, targetY) = command;

            var node = RallyPointOverviewParser.GetOutgoingAttackAbortButton(browser.Html, targetX, targetY);
            if (node is null)
            {
                logger.Warning(
                    "Could not find a cancel button for the dodge attack to ({X}|{Y}) - it may already have been cancelled, or the 90-second cancel window may have closed. It will need to be brought home manually if it's still out.",
                    targetX, targetY);
                return Result.Ok();
            }

            var (_, isFailed, element, errors) = await browser.GetElement(By.XPath(node.XPath), cancellationToken);
            if (isFailed) return Result.Fail(errors);

            var result = await browser.Click(element, cancellationToken);
            if (result.IsFailed) return result;

            logger.Information("Recalled dodge troops sent to ({X}|{Y}).", targetX, targetY);

            return Result.Ok();
        }
    }
}
