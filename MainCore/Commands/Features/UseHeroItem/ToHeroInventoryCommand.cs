#pragma warning disable S1172

namespace MainCore.Commands.Features.UseHeroItem
{
    [Handler]
    public static partial class ToHeroInventoryCommand
    {
        public sealed record Command : ICommand;

        private static async ValueTask<Result> HandleAsync(
            Command command,
            IChromeBrowser browser,
            IDelayService delayService,
            CancellationToken cancellationToken)
        {
            var (_, isFailed, element, errors) = await browser.GetElement(doc => InventoryParser.GetHeroAvatar(doc), cancellationToken);
            if (isFailed) return Result.Fail(errors);

            var result = await browser.Click(element, cancellationToken);
            if (result.IsFailed) return result;

            // 2026-09-12: also accept landing on the dead-hero Attributes screen as a valid
            // wait outcome - clicking the hero avatar while the hero is dead goes there
            // instead of Inventory, and without this the wait below would sit until the
            // WebDriver timeout every single time (see CLAUDE.md note), since IsInventoryPage
            // can never become true in that state.
            static bool TabActived(IWebDriver driver)
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(driver.PageSource);
                return (InventoryParser.IsInventoryPage(doc) && InventoryParser.IsInventoryLoaded(doc)) || HeroParser.IsHeroDead(doc);
            }

            result = await browser.Wait(TabActived, cancellationToken);
            if (result.IsFailed) return result;

            if (HeroParser.IsHeroDead(browser.Html))
            {
                return HeroDead.Error;
            }

            await delayService.DelayTask(cancellationToken);

            return Result.Ok();
        }
    }
}