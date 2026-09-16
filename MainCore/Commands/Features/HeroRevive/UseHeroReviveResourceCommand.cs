using MainCore.Commands.Features.UseHeroItem;

namespace MainCore.Commands.Features.HeroRevive
{
    // Tier 1 of hero revival's 3-tier fallback (2026-09-12, user-requested order: hero's own
    // bag -> sibling villages -> NPC trade with gold - see HeroReviveTask). Feeds the hero's
    // bag resources toward the revive target via the SAME resourceTransferDialog as
    // UseHeroItemCommand (InventoryParser.GetResourceTransferDialog/GetAmountBox/
    // GetConfirmButton/GetSuccessToast) - but opened from the hero's Attributes/dead screen's
    // "revive with resources" icons (HeroParser.GetReviveResourceIcon) instead of the
    // Inventory tab's item grid. Caller is expected to have already validated the hero's bag
    // actually holds at least this much of each resource (see UseHeroItem's own
    // ValidateEnoughResourceCommand) - this command just fills the form and confirms.
    [Handler]
    public static partial class UseHeroReviveResourceCommand
    {
        public sealed record Command(Dictionary<HeroItemEnums, long> ItemToUse) : ICommand;

        private static async ValueTask<Result> HandleAsync(
            Command command,
            IChromeBrowser browser,
            ILogger logger,
            IDelayService delayService,
            CancellationToken cancellationToken)
        {
            var itemToUse = command.ItemToUse.Where(x => x.Value > 0).ToList();
            if (itemToUse.Count == 0) return Result.Ok();

            var result = await OpenReviveDialog(browser, cancellationToken);
            if (result.IsFailed) return result;
            await delayService.DelayClick(cancellationToken);

            foreach (var (item, amount) in itemToUse)
            {
                logger.Information("Use {Amount} {Item} from hero bag toward reviving hero", amount, item);
                result = await UseHeroItemCommand.EnterAmount(browser, item, amount, cancellationToken);
                if (result.IsFailed) return result;
                await delayService.DelayClick(cancellationToken);
            }

            result = await UseHeroItemCommand.Confirm(browser, cancellationToken);
            if (result.IsFailed) return result;
            await delayService.DelayClick(cancellationToken);

            return Result.Ok();
        }

        private static async Task<Result> OpenReviveDialog(IChromeBrowser browser, CancellationToken cancellationToken)
        {
            var (_, isFailed, element, errors) = await browser.GetElement(doc => HeroParser.GetReviveResourceIcon(doc), cancellationToken);
            if (isFailed) return Result.Fail(errors);

            var result = await browser.Click(element, cancellationToken);
            if (result.IsFailed) return result;

            static bool DialogShown(IWebDriver driver)
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(driver.PageSource);
                return InventoryParser.GetResourceTransferDialog(doc) is not null;
            }

            return await browser.Wait(DialogShown, cancellationToken);
        }
    }
}
