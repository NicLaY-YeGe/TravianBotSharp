using Microsoft.Playwright;

namespace MainCore.Services.Playwright
{
    // ⚠️ PROOF OF CONCEPT ONLY - see CLAUDE.md/PROJECT_CONTEXT.md "Playwright göçü" notes
    // (2026-08-15). Deliberately NOT an implementation of IChromeBrowser and NOT wired into
    // the production DI container, dispatcher, or any real Task - the whole point of this
    // class is to prove Playwright can drive the exact same HtmlAgilityPack-based parsers
    // (MapParser, SettleConfirmParser, ...) the real bot already uses, without touching or
    // risking the working Selenium-based bot at all. If the proof succeeds, THIS class gets
    // thrown away and its lessons get folded into a proper IChromeBrowser-shaped
    // implementation as a real (much bigger) migration step.
    //
    // Uses a PERSISTENT context (a real Chromium profile folder on disk, not an incognito
    // session) so a login done once by hand survives between runs - porting the Login flow
    // itself is out of scope for this proof, on purpose, to keep the surface area small.
    public sealed class PlaywrightBrowserPoc : IAsyncDisposable
    {
        private readonly string _profileDir;
        private IPlaywright? _playwright;
        private IBrowserContext? _context;
        private IPage? _page;

        public PlaywrightBrowserPoc(string? profileDir = null)
        {
            _profileDir = profileDir ?? Path.Combine(AppContext.BaseDirectory, "PlaywrightPocProfile");
        }

        public async Task StartAsync()
        {
            Directory.CreateDirectory(_profileDir);

            _playwright = await Microsoft.Playwright.Playwright.CreateAsync();

            // Headed on purpose - the first run needs the user to log in by hand in the
            // window that pops up. Every subsequent run reuses the same profile folder and
            // stays logged in, same idea as a real browser's saved session.
            _context = await _playwright.Chromium.LaunchPersistentContextAsync(_profileDir, new BrowserTypeLaunchPersistentContextOptions
            {
                Headless = false,
            });

            _page = _context.Pages.Count > 0 ? _context.Pages[0] : await _context.NewPageAsync();
        }

        public async Task<Result> NavigateAsync(string url)
        {
            if (_page is null) return Retry.Error.WithError("PlaywrightBrowserPoc.StartAsync() was not called.");

            try
            {
                await _page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
                return Result.Ok();
            }
            catch (Exception ex)
            {
                return Retry.Error.WithError($"Navigate to {url} failed: {ex.Message}");
            }
        }

        // Same shape as IChromeBrowser.Html: parse the LIVE page's current HTML with
        // HtmlAgilityPack so the existing *Parser.cs classes can be reused completely
        // unmodified - this is the crux of the whole proof.
        public async Task<HtmlDocument> GetHtmlAsync()
        {
            var doc = new HtmlDocument();
            if (_page is null) return doc;

            var html = await _page.ContentAsync();
            doc.LoadHtml(html);
            return doc;
        }

        // Mirrors IChromeBrowser's "parse with HtmlAgilityPack, then locate the live element
        // by that node's XPath" pattern - Playwright supports XPath selectors natively via the
        // "xpath=" selector-engine prefix, so this ports over almost verbatim.
        public async Task<Result> ClickByXPathAsync(string xpath)
        {
            if (_page is null) return Retry.Error.WithError("PlaywrightBrowserPoc.StartAsync() was not called.");

            try
            {
                var locator = _page.Locator($"xpath={xpath}");
                await locator.ClickAsync(new LocatorClickOptions { Timeout = 15000 });
                return Result.Ok();
            }
            catch (Exception ex)
            {
                return Retry.Error.WithError($"Click on '{xpath}' failed: {ex.Message}");
            }
        }

        public async Task<Result> FillByXPathAsync(string xpath, string content)
        {
            if (_page is null) return Retry.Error.WithError("PlaywrightBrowserPoc.StartAsync() was not called.");

            try
            {
                var locator = _page.Locator($"xpath={xpath}");
                await locator.FillAsync(content, new LocatorFillOptions { Timeout = 15000 });
                return Result.Ok();
            }
            catch (Exception ex)
            {
                return Retry.Error.WithError($"Fill on '{xpath}' failed: {ex.Message}");
            }
        }

        // No manual polling loop needed the way IChromeBrowser.Wait() does one - this is
        // exactly the "Playwright waits for you" advantage from our earlier discussion.
        // WaitForFunctionAsync re-evaluates the given JS predicate against the live DOM until
        // it's true or the timeout hits, which is the closest built-in equivalent to
        // IChromeBrowser.Wait(Predicate<IWebDriver>) - the two link-appearance/page-transition
        // waits FoundNewVillageCommand needs are done as plain content-polling below instead,
        // since our condition (MapParser.GetFoundNewVillageLink(doc) is not null) lives in C#
        // parser code, not JS, and re-parsing the page's HTML every ~500ms is simple and cheap
        // enough for a proof of concept.
        public async Task<Result> WaitUntilAsync(Func<HtmlDocument, bool> condition, CancellationToken cancellationToken, int timeoutSeconds = 60)
        {
            var deadline = DateTime.Now.AddSeconds(timeoutSeconds);

            while (DateTime.Now < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var doc = await GetHtmlAsync();
                if (condition(doc)) return Result.Ok();

                await Task.Delay(500, cancellationToken);
            }

            return Retry.Error.WithError("Condition was not met within the timeout.");
        }

        public async ValueTask DisposeAsync()
        {
            if (_context is not null) await _context.CloseAsync();
            _playwright?.Dispose();
        }
    }
}
