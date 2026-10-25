using Microsoft.Playwright;
using System.Runtime.CompilerServices;

namespace MainCore.Services
{
    // 2026-10-18: rewritten on Playwright (was Selenium WebDriver + BiDi). The public behaviour is
    // the IChromeBrowser contract the rest of the bot was built against; what changed underneath:
    //
    //  - One persistent Chromium context per account (same Data/Cache/<profile>/<proxy> folder as
    //    before, so cookies/sessions survive). Chromium is Playwright's own build - it is the one
    //    that still honours --load-extension, so the 3 bundled .crx extensions keep working.
    //  - The BiDi "stale context" machinery (RefreshContextAsync, _bidiNavigationBroken, the 750 ms
    //    retry delay, the classic-driver fallback) is gone: Playwright tracks pages itself and
    //    Navigate/Refresh are plain page calls.
    //  - Proxy authentication is native (Proxy.Username/Password) - no BiDi auth intercept.
    //  - Click has actionability checks (visible, stable, not covered). If the normal click is
    //    refused it is retried once with Force, which is what the old coordinate click did.
    //  - GetElement/Wait poll every 250 ms for up to 3 minutes, like the old WebDriverWait, and
    //    time out with the same "Timed out after 180 seconds" text.
    //  - 2026-10-21: Playwright's own timeouts (Click, Goto, Reload, Press...) surface as
    //    System.TimeoutException, which is NOT a PlaywrightException. The catch blocks below only
    //    knew PlaywrightException, so a timed-out Click skipped the Force retry and a timed-out
    //    Goto escaped as a raw exception (live log 13:01:09 / 14:12:51). Every Playwright call
    //    here now also handles System.TimeoutException.
    public sealed class ChromeBrowser : IChromeBrowser
    {
        private static readonly TimeSpan WaitTimeout = TimeSpan.FromMinutes(3);
        private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);
        private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(15);

        private const float NavigationTimeoutMs = 180_000;
        private const float DefaultActionTimeoutMs = 30_000;
        private const float ClickTimeoutMs = 10_000;
        private const float ForceClickTimeoutMs = 5_000;
        private const float ScreenshotTimeoutMs = 10_000;

        private readonly string[] _extensionsPath;
        private readonly HtmlDocument _htmlDoc = new();

        private IBrowserContext? _context;
        private IPage? _page;
        private PlaywrightDriver? _driver;

        public ChromeBrowser(string[] extensionsPath)
        {
            _extensionsPath = extensionsPath;
        }

        public ILogger Logger { get; set; } = null!;

        public async Task Setup(ChromeSetting setting)
        {
            // Drop anything left from a previous run (idempotent) so the profile folder is free.
            await Close();

            var playwright = await PlaywrightHost.GetAsync();

            var args = new List<string>
            {
                "--no-default-browser-check",
                "--no-first-run",
                "--ash-no-nudges",
                "--mute-audio",
                "--disable-gpu",
                "--disable-search-engine-choice-screen",
                "--disable-blink-features=AutomationControlled",
            };

            if (!setting.IsHeadless) args.Add("--start-maximized");

            if (_extensionsPath.Length > 0)
            {
                var paths = string.Join(",", _extensionsPath.Select(Path.GetFullPath));
                args.Add($"--disable-extensions-except={paths}");
                args.Add($"--load-extension={paths}");
            }

            var options = new BrowserTypeLaunchPersistentContextOptions
            {
                Headless = setting.IsHeadless,
                // Extensions only load in full Chromium. Plain headless uses the stripped
                // chromium-headless-shell, which has no extension support - the "chromium"
                // channel is the new headless mode of the full browser.
                Channel = setting.IsHeadless ? "chromium" : null,
                Args = args,
                IgnoreDefaultArgs = ["--enable-automation"],
                IgnoreHTTPSErrors = true,
                ViewportSize = setting.IsHeadless ? new ViewportSize { Width = 1920, Height = 1080 } : ViewportSize.NoViewport,
                Timeout = NavigationTimeoutMs,
            };

            if (!string.IsNullOrEmpty(setting.UserAgent)) options.UserAgent = setting.UserAgent;

            if (!string.IsNullOrEmpty(setting.ProxyHost))
            {
                options.Proxy = new Proxy { Server = $"http://{setting.ProxyHost}:{setting.ProxyPort}" };
                if (!string.IsNullOrEmpty(setting.ProxyUsername) && !string.IsNullOrEmpty(setting.ProxyPassword))
                {
                    options.Proxy.Username = setting.ProxyUsername;
                    options.Proxy.Password = setting.ProxyPassword;
                }
            }

            var pathUserData = Path.Combine(AppContext.BaseDirectory, "Data", "Cache", setting.ProfilePath);
            if (!Directory.Exists(pathUserData)) Directory.CreateDirectory(pathUserData);
            pathUserData = Path.Combine(pathUserData, string.IsNullOrEmpty(setting.ProxyHost) ? "default" : setting.ProxyHost);

            var context = await playwright.Chromium.LaunchPersistentContextAsync(pathUserData, options);
            context.SetDefaultTimeout(DefaultActionTimeoutMs);
            context.SetDefaultNavigationTimeout(NavigationTimeoutMs);

            _context = context;
            _page = context.Pages.FirstOrDefault() ?? await context.NewPageAsync();
            _driver = new PlaywrightDriver(ResolvePage);

            foreach (var path in _extensionsPath)
            {
                Logger.Information("- Loaded extension: {path}", Path.GetFileName(path));
            }
        }

        // TimerManager checks this before running the next queued task so a browser that is gone -
        // closed on purpose (SleepCommand), by hand, crashed, or never launched yet - gets
        // relaunched automatically. A browser that dies underneath us is noticed by the failing
        // call (BrowserException / BrowserClosed), same as before.
        public bool IsOpen => _context is not null;

        public IWebDriver? Driver => _driver;

        public string CurrentUrl => ResolvePage()?.Url ?? "";

        public HtmlDocument Html
        {
            get
            {
                if (_driver is not null) _htmlDoc.LoadHtml(_driver.PageSource);
                return _htmlDoc;
            }
        }

        public async Task Shutdown()
        {
            await Close();
        }

        public async Task Close()
        {
            var context = _context;

            // Clear first so IsOpen is false immediately, whether Close() was deliberate
            // (SleepCommand's cycle) or recovery from finding the browser already dead.
            _context = null;
            _page = null;
            _driver = null;

            if (context is null) return;

            try
            {
                await context.CloseAsync().WaitAsync(CloseTimeout);
            }
            catch
            {
                // ignore - already gone, or hung; nothing more to do with it
            }
        }

        public async Task<string> Screenshot()
        {
            try
            {
                var page = ResolvePage();
                var fileName = Path.Combine(AppContext.BaseDirectory, "Screenshots", $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png");
                Directory.CreateDirectory(Path.GetDirectoryName(fileName)!);
                if (page is null) return "";

                await page.ScreenshotAsync(new() { Path = fileName, Timeout = ScreenshotTimeoutMs });
                return fileName;
            }
            catch (Exception ex)
            {
                // A live context does not mean the browser is still there - closed by hand or
                // crashed, the capture throws. This must never escape: it is called from
                // TimerManager's async timer handler, where an unhandled exception takes the whole
                // app down instead of just pausing the account (see 2026-08-25).
                Logger?.Warning("Could not capture screenshot, browser is likely gone: {Message}", ex.Message);
                return "";
            }
        }

        public async Task<Result> Navigate(string url, CancellationToken cancellationToken)
        {
            var page = ResolvePage();
            if (page is null) return Stop.DriverNotReady;

            try
            {
                await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.Load, Timeout = NavigationTimeoutMs }).WaitAsync(cancellationToken);
                return Result.Ok();
            }
            catch (OperationCanceledException)
            {
                return Cancel.Error;
            }
            catch (PlaywrightException ex) when (IsBrowserGone(ex))
            {
                await Close();
                return BrowserClosed.Error;
            }
            catch (System.TimeoutException ex)
            {
                return Retry.Error.WithError($"Navigation to {url} timed out: {FirstLine(ex.Message)}");
            }
            catch (PlaywrightException ex)
            {
                return Retry.Error.WithError($"Navigation to {url} failed: {FirstLine(ex.Message)}");
            }
        }

        public async Task<Result> Refresh(CancellationToken cancellationToken)
        {
            var page = ResolvePage();
            if (page is null) return Stop.DriverNotReady;

            try
            {
                await page.ReloadAsync(new() { WaitUntil = WaitUntilState.Load, Timeout = NavigationTimeoutMs }).WaitAsync(cancellationToken);
                return Result.Ok();
            }
            catch (OperationCanceledException)
            {
                return Cancel.Error;
            }
            catch (PlaywrightException ex) when (IsBrowserGone(ex))
            {
                await Close();
                return BrowserClosed.Error;
            }
            catch (System.TimeoutException ex)
            {
                return Retry.Error.WithError($"Refresh timed out: {FirstLine(ex.Message)}");
            }
            catch (PlaywrightException ex)
            {
                return Retry.Error.WithError($"Refresh failed: {FirstLine(ex.Message)}");
            }
        }

        public async Task<Result<IWebElement>> GetElement(By by, CancellationToken cancellationToken, [CallerArgumentExpression("by")] string? expression = null)
        {
            return await PollForElement(driver =>
            {
                var elements = driver.FindElements(by);
                if (elements.Count == 0) return null;
                var element = elements[0];
                if (!element.Displayed || !element.Enabled) return null;
                return element;
            }, cancellationToken, expression);
        }

        public async Task<Result<IWebElement>> GetElement(Func<HtmlDocument, HtmlNode?> nodeGenerator, CancellationToken cancellationToken, [CallerArgumentExpression("nodeGenerator")] string? expression = null)
        {
            return await PollForElement(driver =>
            {
                var htmlDoc = new HtmlDocument();
                htmlDoc.LoadHtml(driver.PageSource);

                var node = nodeGenerator(htmlDoc);
                if (node is null) return null;

                var elements = driver.FindElements(By.XPath(node.StableXPath()));
                if (elements.Count == 0) return null;
                var element = elements[0];
                if (!element.Displayed || !element.Enabled) return null;
                return element;
            }, cancellationToken, expression);
        }

        public async Task<Result> Click(IWebElement element, CancellationToken cancellationToken)
        {
            if (_context is null) return Stop.DriverNotReady;
            if (element is not PlaywrightElement target) return Stop.Error.WithError("Click needs an element returned by IChromeBrowser.");

            try
            {
                await target.Locator.ClickAsync(new() { Timeout = ClickTimeoutMs }).WaitAsync(cancellationToken);
                return Result.Ok();
            }
            catch (OperationCanceledException)
            {
                return Cancel.Error;
            }
            catch (PlaywrightException ex) when (!IsBrowserGone(ex))
            {
                // Typically "not stable" (an animation never settles) or "intercepted" (something
                // is on top). The old Selenium click was a plain coordinate click that ignored all
                // of that, and pages like Travian's keep a few such elements, so fall back to it.
                Logger?.Warning("Click was not accepted normally ({Message}) - retrying without the actionability checks.", FirstLine(ex.Message));
            }
            catch (System.TimeoutException ex)
            {
                // Same fall-through as "not accepted": the normal click timed out (element not
                // stable / covered / not found yet). One forced attempt follows.
                Logger?.Warning("Click timed out ({Message}) - retrying without the actionability checks.", FirstLine(ex.Message));
            }
            catch (PlaywrightException)
            {
                await Close();
                return BrowserClosed.Error;
            }

            try
            {
                await target.Locator.ClickAsync(new() { Force = true, Timeout = ForceClickTimeoutMs }).WaitAsync(cancellationToken);
                return Result.Ok();
            }
            catch (OperationCanceledException)
            {
                return Cancel.Error;
            }
            catch (PlaywrightException ex) when (IsBrowserGone(ex))
            {
                await Close();
                return BrowserClosed.Error;
            }
            catch (System.TimeoutException ex)
            {
                // Still nothing to click after the forced attempt: the element left the page
                // (re-render / navigation) between GetElement and Click. Retry re-resolves it.
                return Retry.Error.WithError($"Click timed out, the element is probably no longer on the page: {FirstLine(ex.Message)}");
            }
            catch (PlaywrightException ex)
            {
                return Retry.Error.WithError($"Click failed: {FirstLine(ex.Message)}");
            }
        }

        public async Task<Result> Input(IWebElement element, string content, CancellationToken cancellationToken)
        {
            if (_context is null) return Stop.DriverNotReady;
            if (element is not PlaywrightElement target) return Stop.Error.WithError("Input needs an element returned by IChromeBrowser.");

            try
            {
                // Select the line and type over it - real key events, not Fill(), because the game's
                // forms listen to keyup (see the Defend/Donate notes in CLAUDE.md).
                await target.Locator.PressAsync("Home").WaitAsync(cancellationToken);
                await target.Locator.PressAsync("Shift+End").WaitAsync(cancellationToken);
                await target.Locator.PressSequentiallyAsync(content).WaitAsync(cancellationToken);
                return Result.Ok();
            }
            catch (OperationCanceledException)
            {
                return Cancel.Error;
            }
            catch (PlaywrightException ex) when (IsBrowserGone(ex))
            {
                await Close();
                return BrowserClosed.Error;
            }
            catch (System.TimeoutException ex)
            {
                return Retry.Error.WithError($"Typing into the element timed out: {FirstLine(ex.Message)}");
            }
            catch (PlaywrightException ex)
            {
                return Retry.Error.WithError($"Typing into the element failed: {FirstLine(ex.Message)}");
            }
        }

        public async Task<Result> ExecuteJsScript(string javascript)
        {
            var page = ResolvePage();
            if (page is null) return Stop.DriverNotReady;

            try
            {
                // Selenium's ExecuteScript ran the text as a function BODY ("var x=..;" statements,
                // a trailing "return false;"). Playwright's Evaluate takes an expression, so wrap
                // it, and discard the return value so a non-serializable result cannot throw.
                await page.EvaluateAsync($"(() => {{ (function() {{\n{javascript}\n}})(); return null; }})()");
                return Result.Ok();
            }
            catch (PlaywrightException ex) when (IsBrowserGone(ex))
            {
                await Close();
                return BrowserClosed.Error;
            }
            catch (PlaywrightException ex)
            {
                return Retry.Error.WithError($"Script failed: {FirstLine(ex.Message)}");
            }
        }

        public async Task<Result<int>> CloseExtraTabs()
        {
            var context = _context;
            var main = ResolvePage();
            if (context is null || main is null) return Stop.DriverNotReady;

            try
            {
                var extra = context.Pages.Where(x => x != main && !x.IsClosed).ToList();
                foreach (var page in extra)
                {
                    await page.CloseAsync();
                }
                return Result.Ok(extra.Count);
            }
            catch (PlaywrightException ex)
            {
                return Retry.Error.WithError($"Could not close the extra tabs: {FirstLine(ex.Message)}");
            }
        }

        public async Task<Result> WaitPageChanged(string url, CancellationToken cancellationToken)
        {
            var result = await Wait(driver => driver.Url.Contains(url), cancellationToken);
            if (result.IsFailed) return result.WithError($"Failed to wait for URL change [{url}], current URL is [{CurrentUrl}]");

            result = await Wait(driver =>
            {
                var logo = driver.FindElements(By.Id("logo"));
                return logo.Count > 0 && logo[0].Displayed && logo[0].Enabled;
            }, cancellationToken);

            if (result.IsFailed) return result.WithError("Failed to wait for logo to be displayed");
            return Result.Ok();
        }

        public async Task<Result> Wait(Predicate<IWebDriver> condition, CancellationToken cancellationToken, [CallerArgumentExpression("condition")] string? expression = null)
        {
            var driver = _driver;
            if (driver is null) return Stop.DriverNotReady;

            var deadline = DateTime.UtcNow + WaitTimeout;
            string? lastError = null;

            try
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (ResolvePage() is null)
                    {
                        await Close();
                        return BrowserClosed.Error;
                    }

                    try
                    {
                        if (await Task.Run(() => condition(driver), cancellationToken)) return Result.Ok();
                    }
                    catch (BrowserException ex)
                    {
                        // page is navigating / re-rendering right now - just look again
                        lastError = ex.Message;
                    }

                    if (DateTime.UtcNow >= deadline) break;
                    await Task.Delay(PollInterval, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                return Cancel.Error;
            }

            return TimedOut(lastError, expression);
        }

        // Polls like the old WebDriverWait: every 250 ms for up to 3 minutes, an element counts only
        // when it is displayed and enabled.
        private async Task<Result<IWebElement>> PollForElement(Func<IWebDriver, IWebElement?> probe, CancellationToken cancellationToken, string? expression)
        {
            var driver = _driver;
            if (driver is null) return Stop.DriverNotReady;

            var deadline = DateTime.UtcNow + WaitTimeout;
            string? lastError = null;

            try
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (ResolvePage() is null)
                    {
                        await Close();
                        return BrowserClosed.Error;
                    }

                    try
                    {
                        var element = await Task.Run(() => probe(driver), cancellationToken);
                        if (element is not null) return Result.Ok(element);
                    }
                    catch (BrowserException ex)
                    {
                        lastError = ex.Message;
                    }

                    if (DateTime.UtcNow >= deadline) break;
                    await Task.Delay(PollInterval, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                return Cancel.Error;
            }

            return TimedOut(lastError, expression);
        }

        private static Result TimedOut(string? lastError, string? expression)
        {
            var message = $"Timed out after {(int)WaitTimeout.TotalSeconds} seconds";
            if (!string.IsNullOrEmpty(lastError)) message += $" (last browser error: {FirstLine(lastError)})";

            var error = Retry.Error.WithError(message);
            if (expression is not null) error = error.WithError(expression);
            return error;
        }

        // The main page, or - if it was closed (ad pop-up that took over, user closed the tab) - the
        // first page that is still open. Null means the whole browser is gone.
        private IPage? ResolvePage()
        {
            var context = _context;
            if (context is null) return null;

            var page = _page;
            if (page is not null && !page.IsClosed) return page;

            try
            {
                page = context.Pages.FirstOrDefault(x => !x.IsClosed);
            }
            catch (PlaywrightException)
            {
                page = null;
            }

            _page = page;
            return page;
        }

        private bool IsBrowserGone(PlaywrightException ex)
        {
            if (ex.Message.Contains("has been closed", StringComparison.OrdinalIgnoreCase)) return true;
            if (ex.Message.Contains("Target closed", StringComparison.OrdinalIgnoreCase)) return true;
            if (ex.Message.Contains("Browser closed", StringComparison.OrdinalIgnoreCase)) return true;
            return ResolvePage() is null;
        }

        // Playwright errors carry a long call log after the first line; the first line is the cause.
        private static string FirstLine(string message)
        {
            var index = message.IndexOfAny(['\r', '\n']);
            return index < 0 ? message : message[..index];
        }
    }
}
