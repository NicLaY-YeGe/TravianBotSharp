namespace MainCore.Commands.Misc
{
    [Handler]
    public static partial class DismissCookieConsentCommand
    {
        public sealed record Command() : ICommand;

        private static async ValueTask<Result> HandleAsync(
            Command command,
            IChromeBrowser browser,
            ILogger logger,
            CancellationToken cancellationToken
            )
        {
            var button = CookieConsentParser.GetAcceptAllButton(browser.Html);
            if (button is null) return Result.Ok();

            logger.Information("Cookie consent popup detected, dismissing it");

            // A normal browser.Click() moves the mouse to the element's screen coordinates
            // and clicks there — since this popup sits on top of everything else, that's
            // fine for the popup itself, but the popup is exactly what we're trying to get
            // OUT of the way, so a JS-level click (bypasses screen position entirely) is
            // used instead, and doubles as the fix for the underlying problem: any button
            // that ends up hidden under this overlay would otherwise eat clicks silently.
            var result = await browser.ExecuteJsScript(
                $"document.querySelector('{CookieConsentParser.AcceptButtonSelector}')?.click();");
            if (result.IsFailed) return result;

            // Best-effort settle time for the overlay's close transition. Not using
            // browser.Wait() here on purpose — its shared WebDriverWait carries a long
            // default timeout meant for real page loads, which would be a bad fit for a
            // "did this popup finish closing" check.
            await Task.Delay(500, cancellationToken);

            return Result.Ok();
        }
    }
}
