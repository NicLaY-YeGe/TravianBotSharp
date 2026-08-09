namespace MainCore.Commands.Features.SmithyUpgrade
{
    [Handler]
    public static partial class SmithyUpgradeCommand
    {
        // troopSlot: 1-10, tribe-relative order (same order shown in the barracks/rally point).
        public sealed record Command(VillageId VillageId, int TroopSlot, TribeEnums Tribe) : IVillageCommand;

        private static async ValueTask<Result> HandleAsync(
            Command command,
            IChromeBrowser browser,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            var (villageId, troopSlot, tribe) = command;

            if (SmithyParser.IsResearchBlockMissing(browser.Html, troopSlot, tribe))
            {
                // The whole research entry for this slot isn't on the page - either the slot
                // number doesn't match this tribe's troop order, this troop's unit-producing
                // building isn't built yet so the game doesn't offer its research at all, or the
                // page didn't parse the way SmithyParser expects. Either way this is worth a
                // Warning, not silence: unlike IsUnavailable below, this isn't a normal/expected
                // state. (Not enough resources yet is NOT this case - see IsUnavailable.)
                logger.Warning("Smithy: no research block found for troop slot {TroopSlot} in {VillageId} - check the configured slot matches this tribe's troop order, and that its unit-producing building is already built.", troopSlot, villageId);
                return Stop.Error.WithError($"Research block for troop slot {troopSlot} not found.");
            }

            if (SmithyParser.IsUnavailable(browser.Html, troopSlot, tribe))
            {
                // Smithy level too low, troop already maxed, not enough resources yet (game
                // shows an ETA + gold "Exchange resources" button instead of Improve in this
                // case), or another research is already running - none of these are errors, just
                // nothing to do right now.
                logger.Information("Smithy upgrade for slot {TroopSlot} in {VillageId} is not available right now.", troopSlot, villageId);
                return Result.Ok();
            }

            var node = SmithyParser.GetImproveButton(browser.Html, troopSlot, tribe);
            if (node is null) return Stop.Error.WithError($"Cannot find the Improve button for troop slot {troopSlot}.");

            var (_, isFailed, element, errors) = await browser.GetElement(By.XPath(node.XPath), cancellationToken);
            if (isFailed) return Stop.Error.WithErrors(errors);

            var result = await browser.Click(element, cancellationToken);
            if (result.IsFailed) return Stop.Error.WithErrors(result.Errors);

            // The Improve button's onclick navigates to the same build.php page with
            // "&action=research&..." appended - a browser.Click() that returns Ok only means
            // WebDriver executed the click without throwing, not that the game server actually
            // accepted the request (e.g. a stale checksum token gets silently rejected). Wait
            // for that specific query param to show up in the URL before believing it worked,
            // the same way HandleUpgradeCommand does for regular building upgrades.
            result = await browser.WaitPageChanged("action=research", cancellationToken);
            if (result.IsFailed) return Stop.Error.WithErrors(result.Errors).WithError($"Clicked Improve for troop slot {troopSlot} but the page never navigated to the research action - the request likely wasn't accepted.");

            // Confirm the click didn't just reload the same idle page silently: a research
            // should now actually be running.
            if (SmithyParser.GetOngoingResearchSecondsRemaining(browser.Html) is null)
            {
                return Stop.Error.WithError($"Clicked Improve for troop slot {troopSlot} but no research appears to be running afterwards.");
            }

            logger.Information("Started smithy upgrade for troop slot {TroopSlot} in {VillageId}.", troopSlot, villageId);

            return Result.Ok();
        }
    }
}
