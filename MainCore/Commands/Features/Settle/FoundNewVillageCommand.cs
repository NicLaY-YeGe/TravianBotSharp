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
            ToMapCommand.Handler toMapCommand,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            var (villageId, x, y) = command;

            var toMapResult = await toMapCommand.HandleAsync(new(), cancellationToken);
            if (toMapResult.IsFailed) return toMapResult;

            var coordResult = await InputCoordinates(browser, x, y, cancellationToken);
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

        private static async Task<Result> InputCoordinates(IChromeBrowser browser, int x, int y, CancellationToken cancellationToken)
        {
            var (_, xFailed, xElement, xErrors) = await browser.GetElement(doc => MapParser.GetXInput(doc), cancellationToken);
            if (xFailed) return Result.Fail(xErrors);

            var result = await browser.Input(xElement, $"{x}", cancellationToken);
            if (result.IsFailed) return result;

            var (_, yFailed, yElement, yErrors) = await browser.GetElement(doc => MapParser.GetYInput(doc), cancellationToken);
            if (yFailed) return Result.Fail(yErrors);

            return await browser.Input(yElement, $"{y}", cancellationToken);
        }
    }
}
