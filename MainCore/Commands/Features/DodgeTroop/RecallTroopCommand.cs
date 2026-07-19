namespace MainCore.Commands.Features.DodgeTroop
{
    [Handler]
    public static partial class RecallTroopCommand
    {
        public sealed record Command(VillageId VillageId, string TargetVillageName) : IVillageCommand;

        private static async ValueTask<Result> HandleAsync(
            Command command,
            IChromeBrowser browser,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            var node = RallyPointOverviewParser.GetRecallButton(browser.Html, command.TargetVillageName);
            if (node is null)
            {
                // Best-effort: the recall button isn't always visible (Travian hides it for a
                // short window right around arrival, or the movement may already be back).
                // This is a non-critical cleanup step, so we log and move on instead of
                // treating it as a failure that would pause the bot.
                logger.Warning("Could not find a recall button for the dodge reinforcement to {Target}. It may need to be brought home manually.", command.TargetVillageName);
                return Result.Ok();
            }

            var (_, isFailed, element, errors) = await browser.GetElement(By.XPath(node.XPath), cancellationToken);
            if (isFailed) return Result.Fail(errors);

            var result = await browser.Click(element, cancellationToken);
            if (result.IsFailed) return result;

            logger.Information("Recalled dodge troops from {Target}.", command.TargetVillageName);

            return Result.Ok();
        }
    }
}
