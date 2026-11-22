namespace MainCore.Commands.Features.SmithyUpgrade
{
    [Handler]
    public static partial class SmithyUpgradeCommand
    {
        // troopSlot: 1-10, tribe-relative order (same order shown in the barracks/rally point).
        public sealed record Command(VillageId VillageId, int TroopSlot, TribeEnums Tribe) : IVillageCommand;

        // 2026-11-06 (live log 17:56:20): the page was read about a second after the navigation, the research block was
        // not there yet (the same village and slot had been found 10 times before), and the old code answered with Stop -
        // the whole account was paused. Give the page a few seconds to show the block before calling it missing.
        private const int ResearchBlockWaitSeconds = 8;
        private const int ResearchStartWaitSeconds = 10;

        private static async ValueTask<Result> HandleAsync(
            Command command,
            IChromeBrowser browser,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            var (villageId, troopSlot, tribe) = command;

            if (SmithyParser.IsResearchBlockMissing(browser.Html, troopSlot, tribe))
            {
                using var shortWait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                shortWait.CancelAfter(TimeSpan.FromSeconds(ResearchBlockWaitSeconds));
                var blockWait = await browser.Wait(driver =>
                {
                    var doc = new HtmlDocument();
                    doc.LoadHtml(driver.PageSource);
                    return !SmithyParser.IsResearchBlockMissing(doc, troopSlot, tribe);
                }, shortWait.Token);

                // A user pause (outer token) is a pause, not a missing block.
                if (cancellationToken.IsCancellationRequested) return Cancel.Error;
                if (blockWait.IsFailed) await ReportMissingBlockAsync(browser, logger, villageId, troopSlot);
            }

            if (SmithyParser.IsResearchBlockMissing(browser.Html, troopSlot, tribe))
            {
                // The whole research entry for this slot isn't on the page - either the slot
                // number doesn't match this tribe's troop order, this troop's unit-producing
                // building isn't built yet so the game doesn't offer its research at all, or the
                // page didn't parse the way SmithyParser expects. Either way this is worth a
                // Warning, not silence: unlike IsUnavailable below, this isn't a normal/expected
                // state. (Not enough resources yet is NOT this case - see IsUnavailable.)
                // Secondary task (CLAUDE.md rule 4): Skip, never Stop. SmithyUpgradeTask sets the next visit.
                return Skip.Error.WithError($"Research block for troop slot {troopSlot} not found.");
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
            if (node is null) return Skip.Error.WithError($"Cannot find the Improve button for troop slot {troopSlot}.");

            var (_, isFailed, element, errors) = await browser.GetElement(By.XPath(node.StableXPath()), cancellationToken);
            if (isFailed) return Skip.Error.WithErrors(errors);

            var result = await browser.Click(element, cancellationToken);
            if (result.IsFailed) return Skip.Error.WithErrors(result.Errors);

            // 2026-11-22 (live log 01:29-01:57, ts50): the click DID start the research (a timer of 14377 s was running
            // at the next visit), but the old code waited for "action=research" in the URL; the game stays on
            // /building?id=N, so every run ended as a false failure after 45 s. A URL fragment is not proof
            // (CLAUDE.md rule 23); the page's own proof is the research timer, so poll for that instead.
            using var startWait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            startWait.CancelAfter(TimeSpan.FromSeconds(ResearchStartWaitSeconds));
            var started = await browser.Wait(driver =>
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(driver.PageSource);
                return SmithyParser.GetOngoingResearchSecondsRemaining(doc) is not null;
            }, startWait.Token);

            // A user pause (outer token) is a pause, not a failed start.
            if (cancellationToken.IsCancellationRequested) return Cancel.Error;
            if (started.IsFailed)
            {
                return Skip.Error.WithError($"Clicked Improve for troop slot {troopSlot} but no research timer appeared within {ResearchStartWaitSeconds}s - the request likely wasn't accepted.");
            }

            logger.Information("Started smithy upgrade for troop slot {TroopSlot} in {VillageId}.", troopSlot, villageId);

            return Result.Ok();
        }

        // Evidence for the next report: the old Stop path saved only a screenshot of the top of the page.
        private static async Task ReportMissingBlockAsync(IChromeBrowser browser, ILogger logger, VillageId villageId, int troopSlot)
        {
            logger.Warning("Smithy: no research block found for troop slot {TroopSlot} in {VillageId} - check the configured slot matches this tribe's troop order, and that its unit-producing building is already built.", troopSlot, villageId);
            await PageDump.SaveAsync(browser, logger, "smithy-no-research-block");
        }
    }
}
