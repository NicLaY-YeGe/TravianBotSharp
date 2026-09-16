namespace MainCore.Commands.Features.OasisScout
{
    // Navigates to hero/attributes (the same page CheckHeroHealthCommand already uses for
    // health) and reads the hero's current "Fighting strength" attack-power value - see
    // HeroParser.GetAttackPower for where that number lives on the page.
    [Handler]
    public static partial class GetHeroAttackPowerCommand
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
                return HeroParser.IsAttributesPage(doc) && HeroParser.GetAttackPower(doc) is not null;
            }
            result = await browser.Wait(AttributesLoaded, cancellationToken);
            if (result.IsFailed) return Result.Fail(result.Errors);

            var attackPower = HeroParser.GetAttackPower(browser.Html);
            if (attackPower is null) return Retry.Error.WithError("Failed to read hero attack power from hero/attributes page");

            return attackPower.Value;
        }
    }
}
