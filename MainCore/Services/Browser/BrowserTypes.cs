namespace MainCore.Services
{
    // 2026-10-18: Selenium -> Playwright migration.
    //
    // These are the engine-independent types the whole code base programs against. They keep the
    // names (By, IWebElement, IWebDriver) and the small API surface the ~30 call sites already
    // used, so those files did not have to change. The only implementation is Playwright-based
    // (PlaywrightElement / PlaywrightDriver in PlaywrightAdapters.cs). Nothing in here references
    // Microsoft.Playwright, which keeps MainCore.Test and every parser independent of the engine.

    /// <summary>Locator for an element. Converted to a Playwright selector string.</summary>
    public sealed class By
    {
        private readonly string _description;

        private By(string selector, string description)
        {
            Selector = selector;
            _description = description;
        }

        /// <summary>Playwright selector ("xpath=..." / "css=...").</summary>
        public string Selector { get; }

        public static By XPath(string xpath) => new($"xpath={xpath}", $"By.XPath: {xpath}");

        public static By CssSelector(string css) => new($"css={css}", $"By.CssSelector: {css}");

        public static By Id(string id) => new($"css=[id=\"{EscapeAttribute(id)}\"]", $"By.Id: {id}");

        public static By Name(string name) => new($"css=[name=\"{EscapeAttribute(name)}\"]", $"By.Name: {name}");

        public override string ToString() => _description;

        private static string EscapeAttribute(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    /// <summary>
    /// One element on the live page. Backed by a Playwright locator, so it is re-resolved on every
    /// use - it never goes "stale" the way a Selenium element did.
    /// </summary>
    public interface IWebElement
    {
        bool Displayed { get; }

        bool Enabled { get; }

        string Text { get; }

        /// <summary>
        /// Current value for "value" (what is typed in the box right now, not the HTML attribute),
        /// otherwise the HTML attribute. Null when absent.
        /// </summary>
        string? GetAttribute(string name);

        /// <summary>Types the text key by key (keydown/keypress/input/keyup fire, as with a real keyboard).</summary>
        void SendKeys(string text);

        void Clear();
    }

    /// <summary>
    /// Read-only view of the page used inside <see cref="IChromeBrowser.Wait"/> predicates and for
    /// the few places that need a quick synchronous look at the DOM. Calls block until the browser
    /// answers; they are meant to run on a thread-pool thread (Wait/GetElement already do that).
    /// </summary>
    public interface IWebDriver
    {
        /// <summary>The current DOM serialized to HTML (the live DOM, not the original response).</summary>
        string PageSource { get; }

        string Url { get; }

        IReadOnlyList<IWebElement> FindElements(By by);
    }

    /// <summary>
    /// The browser is gone or the page cannot be reached (closed window, crashed process, closed
    /// context). AccountTaskBehavior turns this into BrowserClosed so TimerManager relaunches it.
    /// </summary>
    public class BrowserException : Exception
    {
        public BrowserException(string message) : base(message)
        {
        }

        public BrowserException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }

    /// <summary>Drop-down helper (replaces Selenium's SelectElement). Fires the page's input/change events.</summary>
    public sealed class SelectElement(IWebElement element)
    {
        public void SelectByValue(string value)
        {
            if (element is not PlaywrightElement playwrightElement)
            {
                throw new ArgumentException("Only elements returned by IChromeBrowser can be used here.", nameof(element));
            }

            playwrightElement.SelectByValue(value);
        }
    }
}
