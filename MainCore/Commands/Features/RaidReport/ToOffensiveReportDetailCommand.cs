namespace MainCore.Commands.Features.RaidReport
{
    // Opens one report from the /report/offensive list. The list's subject links are relative
    // ("?id=19345928|75e76c3b&s=1", resolved against /report/offensive by the browser), so the
    // absolute URL is rebuilt here from the browser's current host. Opening a report marks it as
    // read in the game - harmless for this bot, which tracks reports by id, not by read state.
    [Handler]
    public static partial class ToOffensiveReportDetailCommand
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
            var url = href.StartsWith('?') ? $"{host}/report/offensive{href}"
                : href.StartsWith('/') ? $"{host}{href}"
                : $"{host}/report/offensive?{href}";

            var result = await browser.Navigate(url, cancellationToken);
            if (result.IsFailed) return result;

            static bool PageLoaded(IWebDriver driver)
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(driver.PageSource);
                return OffensiveReportParser.IsOffensiveReportDetailPage(doc);
            }

            result = await browser.Wait(PageLoaded, cancellationToken);
            if (result.IsFailed) return result;

            return Result.Ok();
        }
    }
}
