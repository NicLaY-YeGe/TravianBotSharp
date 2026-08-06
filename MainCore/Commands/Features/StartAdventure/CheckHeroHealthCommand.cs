#pragma warning disable S1172

namespace MainCore.Commands.Features.StartAdventure
{
    [Handler]
    public static partial class CheckHeroHealthCommand
    {
        public sealed record Command : ICommand;

        private static async ValueTask<Result<int>> HandleAsync(
            Command command,
            IChromeBrowser browser,
            CancellationToken cancellationToken)
        {
            var currentUrl = new Uri(browser.CurrentUrl);
            var host = currentUrl.GetLeftPart(UriPartial.Authority);

            var result = await browser.Navigate($"{host}/hero/attributes", cancellationToken);
            if (result.IsFailed) return Result.Fail(result.Errors);

            static bool AttributesLoaded(IWebDriver driver)
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(driver.PageSource);
                return HeroParser.IsAttributesPage(doc) && HeroParser.GetHealthPercent(doc) is not null;
            }
            result = await browser.Wait(AttributesLoaded, cancellationToken);
            if (result.IsFailed) return Result.Fail(result.Errors);

            var healthPercent = HeroParser.GetHealthPercent(browser.Html);
            if (healthPercent is null) return Retry.Error.WithError("Failed to read hero health percent from hero/attributes page");

            return healthPercent.Value;
        }
    }
}
