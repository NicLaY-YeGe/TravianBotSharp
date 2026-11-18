namespace MainCore.Commands.Navigate
{
    [Handler]
    public static partial class SwitchTabCommand
    {
        public sealed record Command(int TabIndex) : ICommand;

        private static async ValueTask<Result> HandleAsync(
           Command command,
           IChromeBrowser browser,
           CancellationToken cancellationToken
           )
        {
            return await SwitchTab(browser, command.TabIndex, cancellationToken);
        }

        public static async ValueTask<Result> SwitchTab(
            IChromeBrowser browser,
            int tabIndex,
            CancellationToken cancellationToken)
        {
            // One snapshot for both checks (browser.Html re-reads the live DOM on every access).
            var html = browser.Html;
            var tab = BuildingTabParser.TryGetTab(html, tabIndex);
            if (tab is null)
            {
                var count = BuildingTabParser.CountTab(html);
                return Retry.Error.WithError($"Found {count} tabs but need tab #{tabIndex + 1} active");
            }

            if (BuildingTabParser.IsTabActive(tab)) return Result.Ok();

            var tabXPath = tab.StableXPath();
            var (_, isFailed, element, errors) = await browser.GetElement(By.XPath(tabXPath), cancellationToken);
            if (isFailed) return Result.Fail(errors).WithError($"Failed to find tab element [{tabXPath}]");

            Result result;
            result = await browser.Click(element, cancellationToken);
            if (result.IsFailed) return result;

            // 2026-10-21: the tabs are real page loads, and Playwright's Click returns as soon as
            // the click is dispatched (Selenium's waited for the load). For a moment the browser
            // therefore shows a page without a tab bar; this predicate used to call GetTab there
            // and died with ArgumentOutOfRangeException, which Wait does not catch (live log
            // 13:01:43, 13:02:30, 14:11:58, 14:13:38 - every Oasis scout attempt). "No such tab
            // yet" now simply means "not active yet", and Wait polls again.
            bool tabActived(IWebDriver driver)
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(driver.PageSource);
                var current = BuildingTabParser.TryGetTab(doc, tabIndex);
                return current is not null && BuildingTabParser.IsTabActive(current);
            }

            result = await browser.Wait(tabActived, cancellationToken);
            if (result.IsFailed) return result;

            return Result.Ok();
        }
    }
}