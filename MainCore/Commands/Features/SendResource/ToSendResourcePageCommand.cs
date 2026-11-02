namespace MainCore.Commands.Features.SendResource
{
    [Handler]
    public static partial class ToSendResourcePageCommand
    {
        public sealed record Command(VillageId VillageId) : IVillageCommand;

        // Index of the "Send resources" tab inside the Marketplace building page.
        // Tab 0 is the overview (where the NPC "Exchange resources" dialog lives),
        // tab 1 is "Send resources". Verified against real page captures (TR + EN clients).
        private const int SendResourceTabIndex = 1;
        private const int MerchantInfoWaitSeconds = 10;

        private static async ValueTask<Result> HandleAsync(
            Command command,
            ToDorfCommand.Handler toDorfCommand,
            UpdateBuildingCommand.Handler updateBuildingCommand,
            ToBuildingByTypeCommand.Handler toBuildingCommand,
            SwitchTabCommand.Handler switchTabCommand,
            IChromeBrowser browser,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            var villageId = command.VillageId;

            var result = await toDorfCommand.HandleAsync(new(2), cancellationToken);
            if (result.IsFailed) return result;

            result = await updateBuildingCommand.HandleAsync(new(villageId), cancellationToken);
            if (result.IsFailed) return result;

            result = await toBuildingCommand.HandleAsync(new(villageId, BuildingEnums.Marketplace), cancellationToken);
            if (result.IsFailed) return result;

            result = await switchTabCommand.HandleAsync(new(SendResourceTabIndex), cancellationToken);
            if (result.IsFailed) return result;

            // 2026-10-25: the active tab is not yet the rendered content (see
            // SendResourceParser.HasMerchantInfo). Wait, under a short limit of its own (the browser's
            // default wait is 180 s, CLAUDE.md rule 9), for the merchant line; when it never shows up
            // the callers read the page as before - at worst the old "no free merchants" answer.
            using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            waitCts.CancelAfter(TimeSpan.FromSeconds(MerchantInfoWaitSeconds));
            var waited = await browser.Wait(driver =>
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(driver.PageSource);
                return SendResourceParser.HasMerchantInfo(doc);
            }, waitCts.Token);

            // a user pause also cancels the wait - surface it as a pause, not as "not rendered"
            cancellationToken.ThrowIfCancellationRequested();

            if (waited.IsFailed)
            {
                logger.Warning("Marketplace: the merchant line did not show up within {Seconds}s - reading the page as it is.", MerchantInfoWaitSeconds);
            }

            return Result.Ok();
        }
    }
}
