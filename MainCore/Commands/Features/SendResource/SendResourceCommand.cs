namespace MainCore.Commands.Features.SendResource
{
    [Handler]
    public static partial class SendResourceCommand
    {
        // VillageId here is the SOURCE village (the one whose merchants will travel).
        // ResourceType is one of: "wood", "clay", "iron", "crop" (matches the game's own input names).
        public sealed record Command(VillageId VillageId, VillageId TargetVillageId, string ResourceType, long Amount) : IVillageCommand;

        private static async ValueTask<Result> HandleAsync(
            Command command,
            IChromeBrowser browser,
            AppDbContext context,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            var (villageId, targetVillageId, resourceType, amount) = command;

            if (amount <= 0) return Result.Ok();

            var targetVillage = context.Villages.FirstOrDefault(x => x.Id == targetVillageId.Value);
            if (targetVillage is null)
            {
                return Retry.Error.WithError($"Cannot find target village {targetVillageId} in the database.");
            }

            var freeMerchants = SendResourceParser.GetFreeMerchants(browser.Html);
            var capacity = SendResourceParser.GetMerchantCapacity(browser.Html);
            if (freeMerchants <= 0)
            {
                return Retry.Error.WithError("No free merchants available in this village right now.");
            }
            if (capacity > 0)
            {
                amount = Math.Min(amount, freeMerchants * capacity);
            }
            if (amount <= 0) return Result.Ok();

            var result = await InputCoordinates(browser, targetVillage.X, targetVillage.Y, cancellationToken);
            if (result.IsFailed) return result;

            result = await InputResourceAmount(browser, resourceType, amount, cancellationToken);
            if (result.IsFailed) return result;

            // give the page's own JS a moment to resolve the target village and
            // enable the send button (it starts out disabled).
            result = await browser.Wait(driver =>
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(driver.PageSource);
                return SendResourceParser.IsSendButtonEnabled(doc);
            }, cancellationToken);
            if (result.IsFailed) return result;

            logger.Information("Sending {Amount} {ResourceType} from village {VillageId} to ({X}|{Y})", amount, resourceType, villageId, targetVillage.X, targetVillage.Y);

            result = await ClickSend(browser, cancellationToken);
            if (result.IsFailed) return result;

            // No confirmation screen on this server - resources are sent immediately.
            // Wait for the free-merchant count to drop as proof the request went through.
            result = await browser.Wait(driver =>
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(driver.PageSource);
                return SendResourceParser.GetFreeMerchants(doc) < freeMerchants;
            }, cancellationToken);
            if (result.IsFailed) return result;

            logger.Information("Merchants sent.");

            return Result.Ok();
        }

        private static async Task<Result> InputCoordinates(IChromeBrowser browser, int x, int y, CancellationToken cancellationToken)
        {
            var xNode = SendResourceParser.GetXInput(browser.Html);
            if (xNode is null) return Retry.Error.WithError("Cannot find X coordinate input.");

            var (_, xFailed, xElement, xErrors) = await browser.GetElement(By.XPath(xNode.XPath), cancellationToken);
            if (xFailed) return Result.Fail(xErrors);

            var result = await browser.Input(xElement, $"{x}", cancellationToken);
            if (result.IsFailed) return result;

            var yNode = SendResourceParser.GetYInput(browser.Html);
            if (yNode is null) return Retry.Error.WithError("Cannot find Y coordinate input.");

            var (_, yFailed, yElement, yErrors) = await browser.GetElement(By.XPath(yNode.XPath), cancellationToken);
            if (yFailed) return Result.Fail(yErrors);

            return await browser.Input(yElement, $"{y}", cancellationToken);
        }

        private static async Task<Result> InputResourceAmount(IChromeBrowser browser, string resourceType, long amount, CancellationToken cancellationToken)
        {
            var node = SendResourceParser.GetResourceInput(browser.Html, resourceType);
            if (node is null) return Retry.Error.WithError($"Cannot find '{resourceType}' amount input.");

            var (_, isFailed, element, errors) = await browser.GetElement(By.XPath(node.XPath), cancellationToken);
            if (isFailed) return Result.Fail(errors);

            return await browser.Input(element, $"{amount}", cancellationToken);
        }

        private static async Task<Result> ClickSend(IChromeBrowser browser, CancellationToken cancellationToken)
        {
            var node = SendResourceParser.GetSendButton(browser.Html);
            if (node is null) return Retry.Error.WithError("Cannot find send button.");

            var (_, isFailed, element, errors) = await browser.GetElement(By.XPath(node.XPath), cancellationToken);
            if (isFailed) return Result.Fail(errors);

            return await browser.Click(element, cancellationToken);
        }
    }
}
