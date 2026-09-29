namespace MainCore.Commands.Features.RaidReport
{
    // Opens one report from the /report/scouting list - same relative-href-resolution approach
    // as ToOffensiveReportDetailCommand.
    [Handler]
    public static partial class ToScoutReportDetailCommand
    {
        public sealed record Command(string Href) : ICommand;

        private static async ValueTask<Result> HandleAsync(
            Command command,
            IChromeBrowser browser,
            CancellationToken cancellationToken)
        {
            var currentUrl = new Uri(browser.CurrentUrl);
            var host = currentUrl.GetLeftPart(UriPartial.Authority);

            var href = command.Href;
            var url = href.StartsWith('?') ? $"{host}/report/scouting{href}"
                : href.StartsWith('/') ? $"{host}{href}"
                : $"{host}/report/scouting?{href}";

            var result = await browser.Navigate(url, cancellationToken);
            if (result.IsFailed) return result;

            static bool PageLoaded(IWebDriver driver)
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(driver.PageSource);
                return ScoutReportParser.IsScoutReportDetailPage(doc);
            }

            result = await browser.Wait(PageLoaded, cancellationToken);
            if (result.IsFailed) return result;

            return Result.Ok();
        }
    }
}
