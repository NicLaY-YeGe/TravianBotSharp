namespace MainCore.Commands.Features.OasisScout
{
    // Navigates directly to /report/surrounding - same "build the URL from the browser's
    // current host" approach already used by CheckHeroHealthCommand/ToMapCommand.
    [Handler]
    public static partial class ToSurroundingReportPageCommand
    {
        public sealed record Command : ICommand;

        private static async ValueTask<r> HandleAsync(
            Command command,
            IChromeBrowser browser,
            CancellationToken cancellationToken)
        {
            var currentUrl = new Uri(browser.CurrentUrl);
            var host = currentUrl.GetLeftPart(UriPartial.Authority);

            var result = await browser.Navigate($"{host}/report/surrounding", cancellationToken);
            if (result.IsFailed) return result;

            static bool PageLoaded(IWebDriver driver)
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(driver.PageSource);
                return SurroundingReportParser.IsSurroundingReportPage(doc);
            }

            result = await browser.Wait(PageLoaded, cancellationToken);
            if (result.IsFailed) return result;

            return Result.Ok();
        }
    }
}
