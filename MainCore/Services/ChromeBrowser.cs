using OpenQA.Selenium.BiDi;
using OpenQA.Selenium.BiDi.BrowsingContext;
using OpenQA.Selenium.BiDi.Network;
using OpenQA.Selenium.BiDi.WebExtension;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Interactions;
using OpenQA.Selenium.Support.UI;
using System.Runtime.CompilerServices;

namespace MainCore.Services
{
    public sealed class ChromeBrowser : IChromeBrowser
    {
        private ChromeDriver? _driver;
        private readonly ChromeDriverService _chromeService;
        private WebDriverWait _wait = null!;

        private readonly string[] _extensionsPath;
        private readonly HtmlDocument _htmlDoc = new();

        private BiDi? _bidi;

        private BrowsingContext? _context;
        private Intercept? _authIntercept;

        // 2026-09-19, real user log (Revive hero -> CheckHeroHealthCommand -> Navigate to
        // /hero/attributes): the BiDi context this class tracks kept failing with the SAME
        // "no such frame: Context ... not found" id on every attempt - across all three Polly
        // retries, over more than a minute - even though the page itself had loaded fine in
        // the browser (screenshot from that moment). A 750 ms wait can't fix a state that
        // doesn't clear on its own, and the exhausted retries ended in Retry.Error, which
        // TimerManager treats as "pause the WHOLE bot". Everything else in this class (click,
        // PageSource, screenshot, ...) goes through the classic WebDriver session, which was
        // healthy the whole time - only Navigate/Refresh use BiDi. So once BiDi navigation has
        // failed twice in a row while the classic session still works, this flag makes
        // Navigate/Refresh use the classic driver directly until the browser is restarted
        // (reset in Setup/Close). See NavigateClassic/RefreshClassic below.
        private bool _bidiNavigationBroken;

        // 2026-09-18: added after a live log showed Navigate's retry (below) failing with the
        // SAME stale context ID as the original failure, even though RefreshContextAsync's
        // GetTreeAsync is a genuine live round-trip to the browser (verified against Selenium's
        // own source - browsingContext.getTree is not client-cached), not a stale local read.
        // That means the browser itself was still reporting the dying context as current -
        // most likely queried mid-teardown, in the brief window between the old context being
        // invalidated and a replacement becoming current. A short pause before retrying gives
        // that transition a moment to settle instead of re-querying into the same race.
        private static readonly TimeSpan ContextRefreshRetryDelay = TimeSpan.FromMilliseconds(750);

        public ChromeBrowser(string[] extensionsPath)
        {
            _extensionsPath = extensionsPath;

            _chromeService = ChromeDriverService.CreateDefaultService();
            _chromeService.HideCommandPromptWindow = true;
        }

        public async Task Setup(ChromeSetting setting)
        {
            var options = new ChromeOptions();

            if (!string.IsNullOrEmpty(setting.ProxyHost))
            {
                options.AddArgument($"--proxy-server={setting.ProxyHost}:{setting.ProxyPort}");
            }

            options.AddArgument($"--user-agent={setting.UserAgent}");
            options.AddArgument("--ignore-certificate-errors");
            options.AddArguments("--no-default-browser-check", "--no-first-run", "--ash-no-nudges");
            options.AddArguments("--mute-audio", "--disable-gpu", "--disable-search-engine-choice-screen");

            options.AddArgument("--enable-unsafe-extension-debugging");
            options.AddArgument("--remote-debugging-pipe");

            options.AddExcludedArgument("enable-automation");
            options.AddAdditionalOption("useAutomationExtension", "undefined");

            options.AddArgument("--disable-background-timer-throttling");
            options.AddArgument("--disable-backgrounding-occluded-windows");
            options.AddArgument("--disable-features=CalculateNativeWinOcclusion");
            options.AddArgument("--disable-features=UserAgentClientHint");
            options.AddArgument("--disable-blink-features=AutomationControlled");

            if (setting.IsHeadless)
            {
                options.AddArgument("--headless=new");
                options.AddArgument("--disable-dev-shm-usage");
            }
            else
            {
                options.AddArgument("--start-maximized");
            }
            var pathUserData = Path.Combine(AppContext.BaseDirectory, "Data", "Cache", setting.ProfilePath);
            if (!Directory.Exists(pathUserData)) Directory.CreateDirectory(pathUserData);

            pathUserData = Path.Combine(pathUserData, string.IsNullOrEmpty(setting.ProxyHost) ? "default" : setting.ProxyHost);

            options.AddArguments($"user-data-dir={pathUserData}");
            options.UseWebSocketUrl = true;
            options.UnhandledPromptBehavior = UnhandledPromptBehavior.Ignore;

            _driver = await Task.Run(() => new ChromeDriver(_chromeService, options, TimeSpan.FromMinutes(3)));

            _driver.Manage().Timeouts().PageLoad = TimeSpan.FromMinutes(3);
            _wait = new WebDriverWait(_driver, TimeSpan.FromMinutes(3)); // watch ads

            _bidi = await _driver.AsBiDiAsync();
            _context = (await _bidi.BrowsingContext.GetTreeAsync()).Contexts[0].Context;
            _bidiNavigationBroken = false;

            foreach (var path in _extensionsPath)
            {
                var result = await _bidi.WebExtension.InstallAsync(new ExtensionPath(path));
                Logger.Information("- Installed extension: {path}", Path.GetFileNameWithoutExtension(path));
            }

            if (!string.IsNullOrEmpty(setting.ProxyHost) && !string.IsNullOrEmpty(setting.ProxyUsername) && !string.IsNullOrEmpty(setting.ProxyPassword))
            {
                _authIntercept = await _bidi.Network.InterceptAuthAsync(async auth =>
                {
                    Logger.Information("- Providing proxy auth credentials", auth.Request.Url);
                    await auth.ContinueAsync(new AuthCredentials(setting.ProxyUsername, setting.ProxyPassword), new ContinueWithAuthCredentialsOptions());
                });
            }
        }

        public ChromeDriver? Driver => _driver;

        // True once Setup() has produced a live driver and stays true until Close() (or the
        // total-death path in RefreshContextAsync, which now also calls Close()) tears it
        // down again. TimerManager checks this before running the next queued task so a
        // browser that's gone - closed by hand, crashed, or never launched yet - gets
        // relaunched automatically instead of the task failing against a dead driver.
        public bool IsOpen => _driver is not null;

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
            if (_driver is null) return;
            await Close();
            _chromeService.Dispose();
        }

        public string CurrentUrl => Driver?.Url ?? "";

        public ILogger Logger { get; set; } = null!;

        public async Task<string> Screenshot()
        {
            try
            {
                var screenshot = Driver?.GetScreenshot();
                var fileName = Path.Combine(AppContext.BaseDirectory, "Screenshots", $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png");
                Directory.CreateDirectory(Path.GetDirectoryName(fileName)!);
                await File.WriteAllBytesAsync(fileName, screenshot?.AsByteArray ?? Array.Empty<byte>(), CancellationToken.None);
                return fileName;
            }
            catch (Exception ex)
            {
                // 2026-08-25: Driver being non-null doesn't mean the browser is still there -
                // if it was closed externally (user closed the window, crash, killed process),
                // GetScreenshot() throws instead of returning null. This call used to be
                // unguarded and the exception escaped all the way up through TimerManager's
                // async timer event handler, which crashes the entire app (no way to catch an
                // exception that escapes an async void event handler) instead of just pausing
                // the account. Swallow it here and let the caller carry on without a screenshot.
                Logger?.Warning("Could not capture screenshot, browser is likely gone: {Message}", ex.Message);
                return "";
            }
        }

        public async Task<Result> Refresh(CancellationToken cancellationToken)
        {
            if (_context is null) return Stop.DriverNotReady;

            if (_bidiNavigationBroken) return await RefreshClassic(cancellationToken);

            try
            {
                await _context.ReloadAsync(new() { Wait = ReadinessState.Complete });
                return Result.Ok();
            }
            catch (BiDiException)
            {
                // 2026-08-25: widened from just "no such frame" - a fully closed browser
                // (whole window/process gone, not just this frame) can surface as a different
                // BiDiException message or as the transport itself failing, and
                // RefreshContextAsync already handles both cases safely either way.
                var refreshResult = await RefreshContextAsync();
                if (refreshResult.IsFailed) return refreshResult;

                // 2026-09-18: see ContextRefreshRetryDelay's own comment - give a possible
                // mid-teardown context transition a moment to finish before retrying.
                await Task.Delay(ContextRefreshRetryDelay, cancellationToken);

                try
                {
                    await _context!.ReloadAsync(new() { Wait = ReadinessState.Complete });
                    return Result.Ok();
                }
                catch (BiDiException retryEx)
                {
                    // 2026-09-17 - same fix as Navigate's retry below, applied here for
                    // consistency: this inner call had no try/catch either, so a second
                    // stale-context hit here would propagate uncaught too.
                    //
                    // 2026-09-19: before giving up, try the classic driver - see
                    // _bidiNavigationBroken's comment for why that's safe and why it works.
                    var classicResult = await RefreshClassic(cancellationToken);
                    if (classicResult.IsSuccess)
                    {
                        _bidiNavigationBroken = true;
                        Logger?.Warning("BiDi refresh failed twice ({Message}) - the classic driver worked, so page reloads will use it from now on.", retryEx.Message);
                        return classicResult;
                    }

                    return Retry.Error
                        .WithError($"Refresh failed twice in a row (context kept going stale): {retryEx.Message}")
                        .WithErrors(classicResult.Errors);
                }
            }
        }

        public async Task<Result> Navigate(string url, CancellationToken cancellationToken)
        {
            if (_context is null) return Stop.DriverNotReady;

            if (_bidiNavigationBroken) return await NavigateClassic(url, cancellationToken);

            try
            {
                await _context.NavigateAsync(url, new() { Wait = ReadinessState.Complete });
                return Result.Ok();
            }
            catch (BiDiException)
            {
                // The browsing context we cached once in Setup() (the tab that was open when the
                // driver started) no longer exists - closed or replaced (crash, popup, manual
                // close, etc). 2026-08-25: a real user log showed this exact
                // "no such frame: Context ... not found" during StartAdventureTask -
                // CheckHeroHealthCommand's Navigate, and since _context never changes on its own,
                // Polly kept retrying the SAME dead context 3 times before permanently pausing the
                // bot - a context that's gone will never come back, so that retry was guaranteed
                // to fail. Re-resolve the CURRENT top-level context from the live browser and
                // retry this navigation once against it instead. If there's truly no context left
                // (the whole browser window is gone), RefreshContextAsync returns BrowserClosed
                // instead of leaving the user to decode a raw BiDi stack trace - TimerManager
                // relaunches Chrome automatically from there (2026-08-25).
                //
                // Widened from just "no such frame" (2026-08-25, second pass) - a fully closed
                // browser (whole window/process gone, not just this frame) can surface as a
                // different BiDiException message, and RefreshContextAsync already handles that
                // case safely too (see its own comments).
                var refreshResult = await RefreshContextAsync();
                if (refreshResult.IsFailed) return refreshResult;

                // 2026-09-18: see ContextRefreshRetryDelay's own comment - give a possible
                // mid-teardown context transition a moment to finish before retrying. This is
                // the exact scenario a real log caught: the retry below hit "no such frame"
                // with the SAME context ID as the original failure, meaning the re-resolve
                // above found the browser still reporting a context that was mid-death.
                await Task.Delay(ContextRefreshRetryDelay, cancellationToken);

                try
                {
                    await _context!.NavigateAsync(url, new() { Wait = ReadinessState.Complete });
                    return Result.Ok();
                }
                catch (BiDiException retryEx)
                {
                    // 2026-09-17, real user log: the FIRST retry above (right after re-resolving
                    // _context) hit "no such frame" a SECOND time, and because this inner
                    // NavigateAsync call had no try/catch of its own, the exception propagated
                    // all the way up uncaught and paused the whole bot - exactly the failure
                    // mode the surrounding comment describes fixing, just one level too shallow.
                    // A context that goes stale twice in a row (screenshot from that same moment
                    // showed the destination page had actually loaded fine, so this looks like a
                    // transient BiDi context-tracking race rather than a truly dead browser) is
                    // still just a transient failure, not a reason to stop the account - return
                    // Retry.Error the same way CheckHeroHealthCommand's own health-read failure
                    // does, so the existing Polly-based task retry (see the "will retry after
                    // ..." log lines) handles it instead of an unhandled exception reaching
                    // TimerManager as a hard pause.
                    //
                    // 2026-09-19: BEFORE returning that Retry, try the classic driver once - a
                    // real log showed this state does NOT clear on its own (same context id on
                    // every retry for over a minute, until the retries ran out and the whole
                    // bot paused) while the classic session and the page itself were fine. See
                    // _bidiNavigationBroken's comment. Only if the classic driver fails too is
                    // the browser genuinely in trouble, and only then does the old Retry apply.
                    var classicResult = await NavigateClassic(url, cancellationToken);
                    if (classicResult.IsSuccess)
                    {
                        _bidiNavigationBroken = true;
                        Logger?.Warning("BiDi navigation failed twice ({Message}) - the classic driver worked, so navigation will use it from now on.", retryEx.Message);
                        return classicResult;
                    }

                    return Retry.Error
                        .WithError($"Navigate failed twice in a row (context kept going stale): {retryEx.Message}")
                        .WithErrors(classicResult.Errors);
                }
            }
        }

        // 2026-09-19: classic-WebDriver equivalents of Navigate/Refresh, used as the fallback
        // (and, once _bidiNavigationBroken is set, the primary path) - see that field's comment.
        // GoToUrl/Refresh block until the page's load event, same as the BiDi calls'
        // ReadinessState.Complete, and the PageLoad timeout set in Setup applies to them.
        private async Task<Result> NavigateClassic(string url, CancellationToken cancellationToken)
        {
            var driver = _driver;
            if (driver is null) return Stop.DriverNotReady;

            try
            {
                await Task.Run(() => driver.Navigate().GoToUrl(url), cancellationToken);
                return Result.Ok();
            }
            catch (WebDriverException ex)
            {
                return Retry.Error.WithError($"Classic navigation to {url} failed: {ex.Message}");
            }
        }

        private async Task<Result> RefreshClassic(CancellationToken cancellationToken)
        {
            var driver = _driver;
            if (driver is null) return Stop.DriverNotReady;

            try
            {
                await Task.Run(() => driver.Navigate().Refresh(), cancellationToken);
                return Result.Ok();
            }
            catch (WebDriverException ex)
            {
                return Retry.Error.WithError($"Classic refresh failed: {ex.Message}");
            }
        }

        // Re-resolves _context to whatever the browser's current top-level browsing context is
        // right now, for recovering after the cached one has been closed/replaced. Used by both
        // Navigate and Refresh above - see their comments for why this exists.
        private async Task<Result> RefreshContextAsync()
        {
            if (_bidi is null)
            {
                await Close();
                return BrowserClosed.Error;
            }

            try
            {
                var contexts = await _bidi.BrowsingContext.GetTreeAsync();
                if (contexts.Contexts.Count == 0)
                {
                    // 2026-08-25: used to return Stop.Error here and tell the user to restart
                    // the bot manually. Now returns BrowserClosed instead - TimerManager
                    // relaunches Chrome automatically on the next tick, so no manual restart
                    // is needed anymore.
                    await Close();
                    return BrowserClosed.Error;
                }

                _context = contexts.Contexts[0].Context;
                return Result.Ok();
            }
            catch
            {
                // The BiDi connection itself is dead (the whole browser process is gone, not
                // just the one tab/frame we were tracking) - GetTreeAsync can throw instead of
                // returning an empty list in that case. Same outcome as the empty-tree branch
                // above: nothing left to recover here.
                await Close();
                return BrowserClosed.Error;
            }
        }

        public async Task<Result<IWebElement>> GetElement(By by, CancellationToken cancellationToken, [CallerArgumentExpression("by")] string? expression = null)
        {
            IWebElement getElement()
            {
                var element = _wait.Until((driver) =>
                {
                    var elements = driver.FindElements(by);
                    if (elements.Count == 0) return null;
                    var element = elements[0];
                    if (!element.Displayed || !element.Enabled) return null;
                    return element;
                }, cancellationToken);
                return element;
            }

            try
            {
                var element = await Task.Run(getElement, cancellationToken);
                return Result.Ok(element);
            }
            catch (OperationCanceledException)
            {
                return Cancel.Error;
            }
            catch (WebDriverTimeoutException ex)
            {
                var error = Retry.Error.WithError(ex.Message);
                if (expression is not null) return error.WithError(expression);
                return error;
            }
        }

        public async Task<Result<IWebElement>> GetElement(Func<HtmlDocument, HtmlNode?> nodeGenerator, CancellationToken cancellationToken, [CallerArgumentExpression("nodeGenerator")] string? expression = null)
        {
            IWebElement getElement()
            {
                var element = _wait.Until((driver) =>
                {
                    var htmlDoc = new HtmlDocument();
                    htmlDoc.LoadHtml(driver.PageSource);

                    var node = nodeGenerator(htmlDoc);
                    if (node is null) return null;

                    var elements = driver.FindElements(By.XPath(node.XPath));
                    if (elements.Count == 0) return null;
                    var element = elements[0];
                    if (!element.Displayed || !element.Enabled) return null;
                    return element;
                }, cancellationToken);
                return element;
            }

            try
            {
                var element = await Task.Run(getElement, cancellationToken);
                return Result.Ok(element);
            }
            catch (OperationCanceledException)
            {
                return Cancel.Error;
            }
            catch (WebDriverTimeoutException ex)
            {
                var error = Retry.Error.WithError(ex.Message);
                if (expression is not null) return error.WithError(expression);
                return error;
            }
        }

        public async Task<Result> Click(IWebElement element, CancellationToken cancellationToken)
        {
            if (Driver is null) return Stop.DriverNotReady;

            await Task.Run(new Actions(Driver).Click(element).Perform);
            return Result.Ok();
        }

        public async Task<Result> Input(IWebElement element, string content, CancellationToken cancellationToken)
        {
            void input()
            {
                element.SendKeys(Keys.Home);
                element.SendKeys(Keys.Shift + Keys.End);
                element.SendKeys(content);
            }

            await Task.Run(input);
            return Result.Ok();
        }

        public async Task<Result> ExecuteJsScript(string javascript)
        {
            if (Driver is null) return Stop.DriverNotReady;
            await Task.CompletedTask;
            var js = Driver as IJavaScriptExecutor;
            js.ExecuteScript(javascript);
            return Result.Ok();
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
            void wait()
            {
                _wait.Until(driver => condition(driver), cancellationToken);
            }

            try
            {
                await Task.Run(wait, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return Cancel.Error;
            }
            catch (WebDriverTimeoutException ex)
            {
                var error = Retry.Error.WithError(ex.Message);
                if (expression is not null) return error.WithError(expression);
                return error;
            }
            return Result.Ok();
        }

        public async Task Close()
        {
            try
            {
                if (_bidi is not null)
                {
                    await _bidi.DisposeAsync();
                }

                await Task.Run(() => _driver?.Quit());
            }
            catch
            {
                // ignore
            }
            finally
            {
                // 2026-08-25: null these out so IsOpen (and RefreshContextAsync's "_bidi is
                // null" check) accurately reflect that nothing usable is left, whether Close()
                // was called on purpose (SleepCommand's cycle) or as recovery from finding the
                // browser already dead. Setup() always overwrites all four fresh, so there's
                // nothing that still needs the old references after this.
                _driver = null;
                _bidi = null;
                _context = null;
                _authIntercept = null;
                _bidiNavigationBroken = false;
            }
        }

        public static class ChromeOptionsExtensions
        {
            private const string background_js = @"
var config = {
	mode: ""fixed_servers"",
    rules: {
        singleProxy: {
            scheme: ""http"",
            host: ""{HOST}"",
            port: parseInt({PORT})
        },
        bypassList: []
	}
};

chrome.proxy.settings.set({ value: config, scope: ""regular"" }, function() { });

function callbackFn(details)
{
	return {
		authCredentials:
		{
			username: ""{USERNAME}"",
			password: ""{PASSWORD}""
		}
	};
}

chrome.webRequest.onAuthRequired.addListener(
	callbackFn,
	{ urls:[""<all_urls>""] },
    ['blocking']
);";

            private const string manifest_json = @"
{
    ""version"": ""1.0.0"",
    ""manifest_version"": 3,
    ""name"": ""Chrome Proxy Authentication"",
    ""permissions"": [
        ""proxy"",
        ""tabs"",
        ""unlimitedStorage"",
        ""storage"",
        ""webRequest"",
        ""webRequestAuthProvider""
    ],
    ""host_permissions"": [
        ""<all_urls>""
    ],
    ""background"": {
        ""service_worker"": ""background.js""
    },
    ""minimum_chrome_version"": ""108""
}";

            /// <summary>
            /// Add HTTP-proxy by <paramref name="userName"/> and <paramref name="password"/>
            /// </summary>
            /// <param name="options">Chrome options</param>
            /// <param name="host">Proxy host</param>
            /// <param name="port">Proxy port</param>
            /// <param name="userName">Proxy username</param>
            /// <param name="password">Proxy password</param>
            public static string CreateHttpProxyExtension(string host, int port, string userName, string password)
            {
                var background_proxy_js = ReplaceTemplates(background_js, host, port, userName, password);

                const string path = "Plugins";
                if (Directory.Exists(path)) Directory.Delete(path);
                Directory.CreateDirectory(path);

                var manifestPath = $"{path}/manifest.json";
                var backgroundPath = $"{path}/background.js";

                File.WriteAllText(manifestPath, manifest_json);
                File.WriteAllText(backgroundPath, background_proxy_js);

                return path;
            }

            private static string ReplaceTemplates(string str, string host, int port, string userName, string password)
            {
                return str
                    .Replace("{HOST}", host)
                    .Replace("{PORT}", port.ToString())
                    .Replace("{USERNAME}", userName)
                    .Replace("{PASSWORD}", password);
            }
        }
    }
}