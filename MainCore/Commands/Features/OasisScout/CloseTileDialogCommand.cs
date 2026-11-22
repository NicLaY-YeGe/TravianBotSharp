namespace MainCore.Commands.Features.OasisScout
{
    // 2026-10-24: closes the tile-detail dialog that ToMapTileCommand opens. Live log 2026-10-24
    // 00:00-07:55: the dialog stayed open while Oasis scout moved on, and at the browser window size in
    // use it covers the top navigation bar, so the next ToDorfCommand click timed out (10 s + 5 s) and
    // then waited 180 s for a URL change - about 20 stalls of 3-9 minutes in 8 hours (screenshot of the
    // open dialog taken at 07:55). The same tile worked at other times, so the cause is intermittent;
    // closing the dialog before leaving the map removes the dependency on it.
    //
    // Never fails: a dialog that cannot be closed is only logged, the caller carries on exactly as
    // before. Every step is bounded (8 s click, 5 s verification, JS fallback).
    [Handler]
    public static partial class CloseTileDialogCommand
    {
        public sealed record Command : ICommand;

        // The close button sits inside the same dialogWrapper as #tileDetails (real capture:
        // `<div class="dialogCancelButton smallIconButton cancel">`).
        private const string CloseButtonCss = ".dialogWrapper:has(#tileDetails) .dialogCancelButton";
        private const string CloseButtonScript = "document.querySelector('.dialogWrapper .dialogCancelButton')?.click();";
        private const int ClickTimeoutSeconds = 8;
        private const int VerifyTimeoutSeconds = 5;

        private static async ValueTask<Result> HandleAsync(
            Command command,
            IChromeBrowser browser,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            if (!OasisTileParser.IsTileDialogOpen(browser.Html)) return Result.Ok();

            // 1) the real close button
            using (var clickCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                clickCts.CancelAfter(TimeSpan.FromSeconds(ClickTimeoutSeconds));
                try
                {
                    var button = await browser.GetElement(By.CssSelector(CloseButtonCss), clickCts.Token);
                    if (!button.IsFailed) await browser.Click(button.Value, clickCts.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // own limit reached - fall through to the checks below
                }
            }

            if (await WaitClosedAsync(browser, cancellationToken)) return Result.Ok();

            // 2) a plain DOM click does not depend on what is drawn on top of the page
            await browser.ExecuteJsScript(CloseButtonScript);
            if (await WaitClosedAsync(browser, cancellationToken)) return Result.Ok();

            logger.Warning("Oasis scout: the tile dialog could not be closed, carrying on (the next navigation may stall).");
            return Result.Ok();
        }

        private static async Task<bool> WaitClosedAsync(IChromeBrowser browser, CancellationToken cancellationToken)
        {
            static bool DialogClosed(IWebDriver driver)
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(driver.PageSource);
                return !OasisTileParser.IsTileDialogOpen(doc);
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(VerifyTimeoutSeconds));
            try
            {
                var result = await browser.Wait(DialogClosed, cts.Token);
                return !result.IsFailed;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return false;
            }
        }
    }
}
