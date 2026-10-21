using Microsoft.Playwright;

namespace MainCore.Services
{
    // Playwright's .NET API is async-only, while the call sites (Wait predicates, element
    // properties, browser.Html) are synchronous by design. Everything here runs the async call on
    // the thread pool and blocks on it, which is safe because none of the callers hold a
    // synchronization context (they all run inside Task.Run / TaskManager timers). Any
    // PlaywrightException - closed page, closed browser, navigation in flight - becomes a
    // BrowserException so callers only have one exception type to think about.
    internal static class SyncBridge
    {
        public static T Run<T>(Func<Task<T>> action)
        {
            try
            {
                return Task.Run(action).GetAwaiter().GetResult();
            }
            catch (PlaywrightException ex)
            {
                throw new BrowserException(ex.Message, ex);
            }
        }

        public static void Run(Func<Task> action)
        {
            try
            {
                Task.Run(action).GetAwaiter().GetResult();
            }
            catch (PlaywrightException ex)
            {
                throw new BrowserException(ex.Message, ex);
            }
        }
    }

    internal sealed class PlaywrightElement(ILocator locator) : IWebElement
    {
        public ILocator Locator { get; } = locator;

        // IsVisible/IsEnabled answer immediately (no auto-wait), which is what the polling loops want.
        public bool Displayed => SyncBridge.Run(() => Locator.IsVisibleAsync());

        public bool Enabled => SyncBridge.Run(() => Locator.IsEnabledAsync());

        public string Text => SyncBridge.Run(() => Locator.InnerTextAsync());

        public string? GetAttribute(string name)
        {
            // Playwright's GetAttribute reads the HTML attribute, which for an <input> is only the
            // INITIAL value - the form checks in SendResourceCommand need what is typed right now.
            if (name == "value")
            {
                return SyncBridge.Run(() => Locator.EvaluateAsync<string?>("el => el.value === undefined || el.value === null ? null : String(el.value)"));
            }

            return SyncBridge.Run(() => Locator.GetAttributeAsync(name));
        }

        public void SendKeys(string text) => SyncBridge.Run(() => Locator.PressSequentiallyAsync(text));

        public void Clear() => SyncBridge.Run(() => Locator.FillAsync(""));

        public void SelectByValue(string value) => SyncBridge.Run(() => Locator.SelectOptionAsync(value));
    }

    internal sealed class PlaywrightDriver(Func<IPage?> pageProvider) : IWebDriver
    {
        private IPage Page => pageProvider() ?? throw new BrowserException("The browser page is not available (closed?).");

        public string PageSource => SyncBridge.Run(() => Page.ContentAsync());

        public string Url => Page.Url;

        public IReadOnlyList<IWebElement> FindElements(By by)
        {
            var locator = Page.Locator(by.Selector);
            var count = SyncBridge.Run(() => locator.CountAsync());

            var elements = new List<IWebElement>(count);
            for (var i = 0; i < count; i++)
            {
                elements.Add(new PlaywrightElement(locator.Nth(i)));
            }
            return elements;
        }
    }

    /// <summary>One shared Playwright driver process for all accounts (each browser is a separate Chromium).</summary>
    internal static class PlaywrightHost
    {
        private static readonly SemaphoreSlim Gate = new(1, 1);
        private static IPlaywright? _instance;

        public static async Task<IPlaywright> GetAsync()
        {
            if (_instance is not null) return _instance;

            await Gate.WaitAsync();
            try
            {
                _instance ??= await Playwright.CreateAsync();
                return _instance;
            }
            finally
            {
                Gate.Release();
            }
        }

        public static async Task DisposeAsync()
        {
            await Gate.WaitAsync();
            try
            {
                _instance?.Dispose();
                _instance = null;
            }
            finally
            {
                Gate.Release();
            }
        }
    }
}
