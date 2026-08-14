namespace MainCore.Commands.Features.Settle
{
    // Navigates directly to the Map page (karte.php) by URL, same "build the URL from the
    // browser's current host" approach already used by CheckHeroHealthCommand.
    [Handler]
    public static partial class ToMapCommand
    {
        public sealed record Command : ICommand;

        private static async ValueTask<Result> HandleAsync(
            Command command,
            IChromeBrowser browser,
            CancellationToken cancellationToken)
        {
            var currentUrl = new Uri(browser.CurrentUrl);
            var host = currentUrl.GetLeftPart(UriPartial.Authority);

            var result = await browser.Navigate($"{host}/karte.php", cancellationToken);
            if (result.IsFailed) return result;

            bool mapLoaded(IWebDriver driver)
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(driver.PageSource);
                return MapParser.GetXInput(doc) is not null && MapParser.GetYInput(doc) is not null;
            }

            result = await browser.Wait(mapLoaded, cancellationToken);
            if (result.IsFailed) return result;

            return Result.Ok();
        }
    }
}
