namespace MainCore.Commands.Features.Celebration
{
    [Handler]
    public static partial class HoldCelebrationCommand
    {
        public const string SmallCelebration = "Small celebration";
        public const string GreatCelebration = "Great celebration";

        public sealed record Command(VillageId VillageId, string CelebrationName) : IVillageCommand;

        private static async ValueTask<Result> HandleAsync(
            Command command,
            IChromeBrowser browser,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            var (villageId, celebrationName) = command;

            if (TownHallParser.IsUnavailable(browser.Html, celebrationName))
            {
                // Not enough resources, already running one, or town hall level too low for
                // this celebration type - none of these are errors, just nothing to do yet.
                logger.Information("{Celebration} is not available right now in {VillageId}.", celebrationName, villageId);
                return Result.Ok();
            }

            var node = TownHallParser.GetHoldButton(browser.Html, celebrationName);
            if (node is null) return Stop.Error.WithError($"Cannot find the Hold button for '{celebrationName}'.");

            var (_, isFailed, element, errors) = await browser.GetElement(By.XPath(node.XPath), cancellationToken);
            if (isFailed) return Stop.Error.WithErrors(errors);

            var result = await browser.Click(element, cancellationToken);
            if (result.IsFailed) return Stop.Error.WithErrors(result.Errors);

            logger.Information("Started {Celebration} in {VillageId}.", celebrationName, villageId);

            return Result.Ok();
        }
    }
}
