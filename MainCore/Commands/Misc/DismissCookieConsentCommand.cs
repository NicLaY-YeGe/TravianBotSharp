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
            // Detection used to go through CookieConsentParser.GetAcceptAllButton, which
            // parses driver.PageSource with HtmlAgilityPack. On some servers (confirmed live
            // via DevTools: document.querySelector('.cmpboxbtnyes') -> null, but
            // #cmpwrapper.shadowRoot.querySelector('.cmpboxbtnyes') -> finds the button) this
            // CMP renders its "Accept all" button inside an OPEN SHADOW ROOT under
            // #cmpwrapper. Shadow DOM content never appears in PageSource, so that parser
            // could never see it there and silently no-op'd forever on those servers.
            // Detection and click are now both done in one JS snippet that checks the
            // shadow-DOM path first, falling back to a plain top-level selector for
            // pages/servers where the button isn't inside a shadow root. ExecuteJsScript has
            // no return channel, so this can't report back whether it actually clicked
            // something — that's fine, it's run unconditionally every cycle and querySelector
            // on a popup that isn't there is a harmless no-op. Deliberately not logging here:
            // this now runs on every single task cycle (called unconditionally from
            // AccountTaskBehavior), so an Information-level line here would spam the log with
            // something that's true almost every time and uninformative when it is.

            // A normal browser.Click() moves the mouse to the element's screen coordinates
            // and clicks there — since this popup sits on top of everything else, that's
            // fine for the popup itself, but the popup is exactly what we're trying to get
            // OUT of the way, so a JS-level click (bypasses screen position entirely) is
            // used instead, and doubles as the fix for the underlying problem: any button
            // that ends up hidden under this overlay would otherwise eat clicks silently.
            var result = await browser.ExecuteJsScript(
                $"(document.querySelector('#cmpwrapper')?.shadowRoot?.querySelector('{CookieConsentParser.AcceptButtonSelector}') " +
                $"?? document.querySelector('{CookieConsentParser.AcceptButtonSelector}'))?.click();");
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
