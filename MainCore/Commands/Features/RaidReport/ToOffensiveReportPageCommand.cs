namespace MainCore.Commands.Features.RaidReport
{
    // Navigates directly to /report/offensive (page 1) or /report/offensive?page=N - same
    // "build the URL from the browser's current host" approach as ToSurroundingReportPageCommand.
    [Handler]
    public static partial class ToOffensiveReportPageCommand
    {
        public sealed record Command(int Page) : ICommand;

        private static async ValueTask<Result> HandleAsync(
            Command command,
            IChromeBrowser browser,
            CancellationToken cancellationToken)
        {
            var currentUrl = new Uri(browser.CurrentUrl);
            var host = currentUrl.GetLeftPart(UriPartial.Authority);

            var url = command.Page <= 1
                ? $"{host}/report/offensive"
                : $"{host}/report/offensive?page={command.Page}";

            var result = await browser.Navigate(url, cancellationToken);
            if (result.IsFailed) return result;

            static bool PageLoaded(IWebDriver driver)
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(driver.PageSource);
                return OffensiveReportParser.IsOffensiveReportListPage(doc);
            }

            result = await browser.Wait(PageLoaded, cancellationToken);
            if (result.IsFailed) return result;

            return Result.Ok();
        }
    }
}
