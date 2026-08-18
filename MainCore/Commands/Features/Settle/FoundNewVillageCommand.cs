namespace MainCore.Commands.Features.Settle
{
    // Enters a target coordinate on the Map page, waits for the "Found new village" link to
    // appear (only shows up for a currently-foundable tile), clicks it, then confirms on the
    // resulting settle screen. See MapParser/SettleConfirmParser for the page-structure notes.
    //
    // Per §2e (post-click verification is mandatory, don't trust Click()'s own success): both
    // the "found new village" click and the final "settle" click are followed by an explicit
    // wait for the page to actually move on, not just assumed to have worked.
    [Handler]
    public static partial class FoundNewVillageCommand
    {
        public sealed record Command(VillageId VillageId, int TargetX, int TargetY) : IVillageCommand;

        private static async ValueTask<Result> HandleAsync(
            Command command,
            IChromeBrowser browser,
            IDelayService delayService,
            ToMapCommand.Handler toMapCommand,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            var (villageId, x, y) = command;

            var toMapResult = await toMapCommand.HandleAsync(new(), cancellationToken);
            if (toMapResult.IsFailed) return toMapResult;

            var coordResult = await InputCoordinates(browser, delayService, x, y, cancellationToken);
            if (coordResult.IsFailed) return coordResult;

            bool linkAppeared(IWebDriver driver)
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(driver.PageSource);
                return MapParser.GetFoundNewVillageLink(doc) is not null;
            }

            var linkWaitResult = await browser.Wait(linkAppeared, cancellationToken);
            if (linkWaitResult.IsFailed)
            {
                return Retry.Error.WithError($"No 'Found new village' link appeared for ({x}|{y}) - the tile may not currently be foundable.");
            }

            var (_, linkFailed, linkElement, linkErrors) = await browser.GetElement(doc => MapParser.GetFoundNewVillageLink(doc), cancellationToken);
            if (linkFailed) return Result.Fail(linkErrors);

            var linkClickResult = await browser.Click(linkElement, cancellationToken);
            if (linkClickResult.IsFailed) return linkClickResult;

            bool onConfirmPage(IWebDriver driver)
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(driver.PageSource);
                return SettleConfirmParser.IsSettleConfirmPage(doc);
            }

            var confirmWaitResult = await browser.Wait(onConfirmPage, cancellationToken);
            if (confirmWaitResult.IsFailed)
            {
                return Stop.Error.WithErrors(confirmWaitResult.Errors)
                    .WithError($"Clicked 'Found new village' for ({x}|{y}) but never reached the settle confirmation screen.");
            }

            var (_, settleFailed, settleElement, settleErrors) = await browser.GetElement(doc => SettleConfirmParser.GetSettleButton(doc), cancellationToken);
            if (settleFailed) return Result.Fail(settleErrors);

            var settleClickResult = await browser.Click(settleElement, cancellationToken);
            if (settleClickResult.IsFailed) return settleClickResult;

            bool leftConfirmPage(IWebDriver driver)
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(driver.PageSource);
                return !SettleConfirmParser.IsSettleConfirmPage(doc);
            }

            var settledWaitResult = await browser.Wait(leftConfirmPage, cancellationToken);
            if (settledWaitResult.IsFailed)
            {
                return Stop.Error.WithErrors(settledWaitResult.Errors)
                    .WithError($"Clicked Settle for ({x}|{y}) but the confirmation screen never went away - the founding likely wasn't accepted.");
            }

            logger.Information("Founded new village at ({X}|{Y}) from village {VillageId}.", x, y, villageId);

            return Result.Ok();
        }

        private static async Task<Result> InputCoordinates(IChromeBrowser browser, IDelayService delayService, int x, int y, CancellationToken cancellationToken)
        {
            var (_, xFailed, xElement, xErrors) = await browser.GetElement(doc => MapParser.GetXInput(doc), cancellationToken);
            if (xFailed) return Result.Fail(xErrors);

            var result = await browser.Input(xElement, $"{x}", cancellationToken);
            if (result.IsFailed) return result;

            var (_, yFailed, yElement, yErrors) = await browser.GetElement(doc => MapParser.GetYInput(doc), cancellationToken);
            if (yFailed) return Result.Fail(yErrors);

            result = await browser.Input(yElement, $"{y}", cancellationToken);
            if (result.IsFailed) return result;

            // CONFIRMED (2026-08-15, real page capture): typing X/Y alone - even with a
            // trailing Enter - does NOT jump the map. A real click on the "Go"/OK button next
            // to the coordinate inputs is required; see MapParser.GetGoButton for why its id
            // can't be hardcoded.
            var (_, goFailed, goElement, goErrors) = await browser.GetElement(doc => MapParser.GetGoButton(doc), cancellationToken);
            if (goFailed) return Result.Fail(goErrors);

            result = await browser.Click(goElement, cancellationToken);
            if (result.IsFailed) return result;

            // ⚠️ CORRECTED (2026-08-17, live failure): a previous version of this fix assumed
            // clicking Go causes a full page reload (based on the form's method="get"
            // action="/karte.php") and waited up to 180s for the coordinate inputs' "value"
            // attribute to be rewritten by the server. That assumption was never actually
            // confirmed - it was inferred from a static HTML snapshot, not from observing the
            // navigation itself - and it turned out wrong: live testing showed the wait always
            // times out, meaning Go most likely triggers a JS/AJAX map shift with no page
            // reload (consistent with the rest of the map being entirely JS-driven), so that
            // attribute is never rewritten at all. Replaced with the same delayService.DelayClick
            // pattern already used elsewhere in this codebase to let a click's AJAX effect settle
            // (see ClaimQuestCommand) - the actual success check remains the linkAppeared wait
            // below, which verifies the real outcome instead of a guessed mechanism.
            await delayService.DelayClick(cancellationToken);

            // CONFIRMED (2026-08-17, real page capture): the map has no per-tile DOM elements
            // (see MapParser.GetMapContainer) - jumping to a coordinate only re-centers the map
            // view on it. The "Found new village" link only appears once the current (center)
            // tile's info dialog is opened (the page sets tileDisplayInformation.type =
            // 'dialog'), which requires an actual left-click - there is no element to click for
            // the tile itself, only the map viewport, whose own center always IS the tile that
            // was just jumped to.
            var (_, mapFailed, mapElement, mapErrors) = await browser.GetElement(doc => MapParser.GetMapContainer(doc), cancellationToken);
            if (mapFailed) return Result.Fail(mapErrors);

            return await browser.Click(mapElement, cancellationToken);
        }
    }
}
