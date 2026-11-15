using System.Runtime.CompilerServices;

namespace MainCore.Services
{
    // 2026-10-18: engine is Playwright now (was Selenium + chromedriver). The name is kept so the
    // ~90 files that depend on it did not have to change. By / IWebElement / IWebDriver are the
    // engine-independent types from Browser/BrowserTypes.cs.
    public interface IChromeBrowser
    {
        string CurrentUrl { get; }
        IWebDriver? Driver { get; }
        bool IsOpen { get; }
        HtmlDocument Html { get; }
        ILogger Logger { get; set; }

        Task<Result> Click(IWebElement element, CancellationToken cancellationToken);

        Task Close();

        /// <summary>Closes every tab/window except the main one (ad pop-ups). The value is how many were closed.</summary>
        Task<Result<int>> CloseExtraTabs();

        /// <summary>Runs the script as a function body in the page (a trailing "return x;" is fine, the value is discarded).</summary>
        Task<Result> ExecuteJsScript(string javascript);

        Task<Result<IWebElement>> GetElement(Func<HtmlDocument, HtmlNode?> nodeGenerator, CancellationToken cancellationToken, [CallerArgumentExpression("nodeGenerator")] string? expression = null);

        Task<Result<IWebElement>> GetElement(By by, CancellationToken cancellationToken, [CallerArgumentExpression("by")] string? expression = null);

        Task<Result> Input(IWebElement element, string content, CancellationToken cancellationToken);

        Task<Result> Navigate(string url, CancellationToken cancellationToken);

        Task<Result> Refresh(CancellationToken cancellationToken);

        Task<string> Screenshot();

        Task Setup(ChromeSetting setting);

        Task Shutdown();

        Task<Result> Wait(Predicate<IWebDriver> condition, CancellationToken cancellationToken, [CallerArgumentExpression("condition")] string? expression = null);

        Task<Result> WaitPageChanged(string url, CancellationToken cancellationToken);
    }
}
