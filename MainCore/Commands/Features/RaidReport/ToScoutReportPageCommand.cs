namespace MainCore.Commands.Features.RaidReport
{
    // Navigates directly to /report/scouting (page 1) or /report/scouting?page=N - same "build
    // the URL from the browser's current host" approach as ToOffensiveReportPageCommand.
    [Handler]
    public static partial class ToScoutReportPageCommand
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
                ? $"{host}/report/scouting"
                : $"{host}/report/scouting?page={command.Page}";

            var result = await browser.Navigate(url, cancellationToken);
            if (result.IsFailed) return result;

            static bool PageLoaded(IWebDriver driver)
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(driver.PageSource);
                return ScoutReportParser.IsScoutReportListPage(doc);
            }

            result = await browser.Wait(PageLoaded, cancellationToken);
            if (result.IsFailed) return result;

            return Result.Ok();
        }
    }
}
