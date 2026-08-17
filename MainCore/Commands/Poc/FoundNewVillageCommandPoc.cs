using MainCore.Services.Playwright;

namespace MainCore.Commands.Poc
{
    // ⚠️ PROOF OF CONCEPT ONLY. Same exact logic as the real
    // MainCore/Commands/Features/Settle/FoundNewVillageCommand.cs, deliberately kept
    // side-by-side line-for-line so the two are easy to diff by eye - the ONLY thing that
    // changed is which browser engine drives it. Every parser call
    // (MapParser.GetXInput/GetYInput/GetGoButton/GetFoundNewVillageLink,
    // SettleConfirmParser.IsSettleConfirmPage/GetSettleButton) is 100% untouched production
    // code - this class proves those don't need to change at all when the browser engine
    // does, since they only ever operate on a parsed HtmlDocument, never on Selenium/Playwright
    // types directly.
    //
    // NOT wired into DI, the dispatcher, or any real Task - triggered only from the Debug tab's
    // "Playwright PoC" button (DebugViewModel.RunPlaywrightPoc) for manual, supervised testing.
    public static class FoundNewVillageCommandPoc
    {
        public static async Task<Result> RunAsync(PlaywrightBrowserPoc browser, int x, int y, CancellationToken cancellationToken)
        {
            var coordResult = await InputCoordinates(browser, x, y, cancellationToken);
            if (coordResult.IsFailed) return coordResult;

            var linkWaitResult = await browser.WaitUntilAsync(
                doc => MapParser.GetFoundNewVillageLink(doc) is not null,
                cancellationToken);
            if (linkWaitResult.IsFailed)
            {
                return Retry.Error.WithError($"No 'Found new village' link appeared for ({x}|{y}) - the tile may not currently be foundable.");
            }

            var doc = await browser.GetHtmlAsync();
            var linkNode = MapParser.GetFoundNewVillageLink(doc);
            if (linkNode is null) return Retry.Error.WithError("Link disappeared between the wait and reading it.");

            var linkClickResult = await browser.ClickByXPathAsync(linkNode.XPath);
            if (linkClickResult.IsFailed) return linkClickResult;

            var confirmWaitResult = await browser.WaitUntilAsync(
                SettleConfirmParser.IsSettleConfirmPage,
                cancellationToken);
            if (confirmWaitResult.IsFailed)
            {
                return Stop.Error.WithErrors(confirmWaitResult.Errors)
                    .WithError($"Clicked 'Found new village' for ({x}|{y}) but never reached the settle confirmation screen.");
            }

            var confirmDoc = await browser.GetHtmlAsync();
            var settleNode = SettleConfirmParser.GetSettleButton(confirmDoc);
            if (settleNode is null) return Retry.Error.WithError("Settle button not found on the confirmation screen.");

            var settleClickResult = await browser.ClickByXPathAsync(settleNode.XPath);
            if (settleClickResult.IsFailed) return settleClickResult;

            var settledWaitResult = await browser.WaitUntilAsync(
                d => !SettleConfirmParser.IsSettleConfirmPage(d),
                cancellationToken);
            if (settledWaitResult.IsFailed)
            {
                return Stop.Error.WithErrors(settledWaitResult.Errors)
                    .WithError($"Clicked Settle for ({x}|{y}) but the confirmation screen never went away - the founding likely wasn't accepted.");
            }

            return Result.Ok();
        }

        private static async Task<Result> InputCoordinates(PlaywrightBrowserPoc browser, int x, int y, CancellationToken cancellationToken)
        {
            var doc = await browser.GetHtmlAsync();

            var xNode = MapParser.GetXInput(doc);
            if (xNode is null) return Retry.Error.WithError("X coordinate input not found.");
            var xResult = await browser.FillByXPathAsync(xNode.XPath, $"{x}");
            if (xResult.IsFailed) return xResult;

            doc = await browser.GetHtmlAsync();
            var yNode = MapParser.GetYInput(doc);
            if (yNode is null) return Retry.Error.WithError("Y coordinate input not found.");
            var yResult = await browser.FillByXPathAsync(yNode.XPath, $"{y}");
            if (yResult.IsFailed) return yResult;

            // Same fact learned live today (2026-08-15): typing alone doesn't jump the map,
            // the "Go"/OK button next to the inputs has to be clicked for real.
            doc = await browser.GetHtmlAsync();
            var goNode = MapParser.GetGoButton(doc);
            if (goNode is null) return Retry.Error.WithError("Go button not found.");

            return await browser.ClickByXPathAsync(goNode.XPath);
        }
    }
}
