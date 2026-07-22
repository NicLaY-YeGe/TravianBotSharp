using OpenQA.Selenium.Support.UI;

namespace MainCore.Commands.Features.Demolish
{
    [Handler]
    public static partial class DemolishCommand
    {
        // Location is the building's slot/location number (matches Building.Location).
        public sealed record Command(VillageId VillageId, int Location) : IVillageCommand;

        private static async ValueTask<Result> HandleAsync(
            Command command,
            IChromeBrowser browser,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            var (villageId, location) = command;

            if (!DemolishParser.HasOption(browser.Html, location))
            {
                // Nothing at this slot to demolish (already gone, or never existed) -
                // not an error, the task will notice and queue the rebuild instead.
                logger.Information("Nothing to demolish at location {Location} in {VillageId}.", location, villageId);
                return Result.Ok();
            }

            var selectNode = DemolishParser.GetSelect(browser.Html);
            if (selectNode is null) return Stop.Error.WithError("Cannot find the demolish dropdown.");

            var (_, selectFailed, selectElement, selectErrors) = await browser.GetElement(By.XPath(selectNode.XPath), cancellationToken);
            if (selectFailed) return Stop.Error.WithErrors(selectErrors);

            var select = new SelectElement(selectElement);
            select.SelectByValue($"{location}");

            var buttonNode = DemolishParser.GetDemolishButton(browser.Html);
            if (buttonNode is null) return Stop.Error.WithError("Cannot find the demolish button.");

            var (_, buttonFailed, buttonElement, buttonErrors) = await browser.GetElement(By.XPath(buttonNode.XPath), cancellationToken);
            if (buttonFailed) return Stop.Error.WithErrors(buttonErrors);

            var result = await browser.Click(buttonElement, cancellationToken);
            if (result.IsFailed) return Stop.Error.WithErrors(result.Errors);

            logger.Information("Queued demolish for location {Location} in {VillageId}.", location, villageId);

            return Result.Ok();
        }
    }
}
