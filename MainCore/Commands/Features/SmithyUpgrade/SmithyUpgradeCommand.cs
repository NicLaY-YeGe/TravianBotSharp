namespace MainCore.Commands.Features.SmithyUpgrade
{
    [Handler]
    public static partial class SmithyUpgradeCommand
    {
        // troopSlot: 1-10, tribe-relative order (same order shown in the barracks/rally point).
        public sealed record Command(VillageId VillageId, int TroopSlot) : IVillageCommand;

        private static async ValueTask<Result> HandleAsync(
            Command command,
            IChromeBrowser browser,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            var (villageId, troopSlot) = command;

            if (SmithyParser.IsUnavailable(browser.Html, troopSlot))
            {
                // Smithy level too low, troop already maxed, or another research is already
                // running - none of these are errors, just nothing to do right now.
                logger.Information("Smithy upgrade for slot {TroopSlot} in {VillageId} is not available right now.", troopSlot, villageId);
                return Result.Ok();
            }

            var node = SmithyParser.GetImproveButton(browser.Html, troopSlot);
            if (node is null) return Stop.Error.WithError($"Cannot find the Improve button for troop slot {troopSlot}.");

            var (_, isFailed, element, errors) = await browser.GetElement(By.XPath(node.XPath), cancellationToken);
            if (isFailed) return Stop.Error.WithErrors(errors);

            var result = await browser.Click(element, cancellationToken);
            if (result.IsFailed) return Stop.Error.WithErrors(result.Errors);

            logger.Information("Started smithy upgrade for troop slot {TroopSlot} in {VillageId}.", troopSlot, villageId);

            return Result.Ok();
        }
    }
}
