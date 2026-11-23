namespace MainCore.Commands.Features.BuyCrop
{
    // 2026-11-10: opens the Marketplace "Buy" tab of the village (dorf2 -> Marketplace -> Buy). The tab is found by its
    // link (href ends with t=1), not by position; waiting for the page is limited to a few seconds of its own because
    // the browser-wide default is 3 minutes (CLAUDE.md rule 9).
    [Handler]
    public static partial class ToMarketplaceBuyPageCommand
    {
        public sealed record Command(VillageId VillageId) : IVillageCommand;

        private const int TabWaitSeconds = 20;
        private const int ContentWaitSeconds = 12;
        private const int TableWaitSeconds = 4;

        private static async ValueTask<Result> HandleAsync(
            Command command,
            ToDorfCommand.Handler toDorfCommand,
            UpdateBuildingCommand.Handler updateBuildingCommand,
            ToBuildingByTypeCommand.Handler toBuildingCommand,
            IChromeBrowser browser,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            try
            {
                return await OpenAsync(command, toDorfCommand, updateBuildingCommand, toBuildingCommand, browser, logger, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return Cancel.Error;
            }
        }

        private static async ValueTask<Result> OpenAsync(
            Command command,
            ToDorfCommand.Handler toDorfCommand,
            UpdateBuildingCommand.Handler updateBuildingCommand,
            ToBuildingByTypeCommand.Handler toBuildingCommand,
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

            // The Buy tab is not the default tab: click it unless it is showing already.
            if (MarketplaceBuyParser.GetContainer(browser.Html) is null)
            {
                var tab = MarketplaceBuyParser.GetBuyTab(browser.Html);
                if (tab is null) return Skip.Error.WithError("The Marketplace page has no Buy tab (no link ending with t=1)");

                using var tabCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                tabCts.CancelAfter(TimeSpan.FromSeconds(TabWaitSeconds));

                var (_, isFailed, element, errors) = await browser.GetElement(By.XPath(tab.StableXPath()), tabCts.Token);
                cancellationToken.ThrowIfCancellationRequested();
                if (isFailed) return Skip.Error.WithErrors(errors).WithError("The Buy tab link could not be found");

                result = await browser.Click(element, tabCts.Token);
                cancellationToken.ThrowIfCancellationRequested();
                if (result.IsFailed) return Skip.Error.WithErrors(result.Errors).WithError("Clicking the Buy tab failed");
            }

            using var contentCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            contentCts.CancelAfter(TimeSpan.FromSeconds(ContentWaitSeconds));
            result = await browser.Wait(driver =>
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(driver.PageSource);
                return MarketplaceBuyParser.GetContainer(doc) is not null && MarketplaceBuyParser.GetFreeMerchants(doc) is not null;
            }, contentCts.Token);
            cancellationToken.ThrowIfCancellationRequested();
            if (result.IsFailed) return Skip.Error.WithErrors(result.Errors).WithError($"The Buy tab did not show its merchant line within {ContentWaitSeconds}s");

            // The offers are drawn right after the header. A village without any offer may never draw the table,
            // so a miss here is not an error - the caller then simply finds no offers.
            using var tableCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            tableCts.CancelAfter(TimeSpan.FromSeconds(TableWaitSeconds));
            var table = await browser.Wait(driver =>
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(driver.PageSource);
                return MarketplaceBuyParser.HasOffersTable(doc);
            }, tableCts.Token);
            cancellationToken.ThrowIfCancellationRequested();
            if (table.IsFailed) logger.Information("Marketplace Buy tab in {VillageId}: the offer table did not show within {Seconds}s (no offers?).", villageId, TableWaitSeconds);

            return Result.Ok();
        }
    }
}
