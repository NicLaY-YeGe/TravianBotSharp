namespace MainCore.Commands.Features.DodgeTroop
{
    [Handler]
    public static partial class SendReinforcementCommand
    {
        private const int ReinforcementEventType = 5;

        // VillageId is the SOURCE village (the one dodging). TroopSlot is 1-10 (tribe-relative,
        // matches the order shown in the village's own barracks/rally point). Sends the FULL
        // available amount of that troop type - dodging means getting them all out of harm's way.
        public sealed record Command(VillageId VillageId, int TroopSlot, VillageId TargetVillageId) : IVillageCommand;

        private static async ValueTask<Result<DateTime?>> HandleAsync(
            Command command,
            IChromeBrowser browser,
            AppDbContext context,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            var (villageId, troopSlot, targetVillageId) = command;

            var targetVillage = context.Villages.FirstOrDefault(x => x.Id == targetVillageId.Value);
            if (targetVillage is null)
            {
                return Retry.Error.WithError($"Cannot find target village {targetVillageId} in the database.");
            }

            var amount = RallyPointSendTroopsParser.GetAvailableTroopCount(browser.Html, troopSlot);
            if (amount <= 0)
            {
                logger.Information("No troops in slot {TroopSlot} to dodge with.", troopSlot);
                return (DateTime?)null;
            }

            var result = await InputTroopAmount(browser, troopSlot, amount, cancellationToken);
            if (result.IsFailed) return result;

            result = await InputCoordinates(browser, targetVillage.X, targetVillage.Y, cancellationToken);
            if (result.IsFailed) return result;

            result = await SelectReinforcement(browser, cancellationToken);
            if (result.IsFailed) return result;

            logger.Information("Dodging {Amount} troops (slot {TroopSlot}) from village {VillageId} to ({X}|{Y})", amount, troopSlot, villageId, targetVillage.X, targetVillage.Y);

            result = await ClickSend(browser, cancellationToken);
            if (result.IsFailed) return result;

            result = await browser.Wait(driver =>
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(driver.PageSource);
                return RallyPointSendTroopsParser.IsConfirmPage(doc);
            }, cancellationToken);
            if (result.IsFailed) return result;

            DateTime? arrivalTime = null;
            var arrival = RallyPointSendTroopsParser.GetArrivalTimestamp(browser.Html);
            if (arrival is not null)
            {
                arrivalTime = DateTimeOffset.FromUnixTimeSeconds(arrival.Value).LocalDateTime;
                logger.Information("Troops will arrive at {ArrivalTime}", arrivalTime);
            }

            result = await ClickConfirm(browser, cancellationToken);
            if (result.IsFailed) return result;

            logger.Information("Troops dodged.");

            return arrivalTime;
        }

        private static async Task<Result> InputTroopAmount(IChromeBrowser browser, int troopSlot, long amount, CancellationToken cancellationToken)
        {
            var node = RallyPointSendTroopsParser.GetTroopInput(browser.Html, troopSlot);
            if (node is null) return Retry.Error.WithError($"Cannot find troop slot {troopSlot} input.");

            var (_, isFailed, element, errors) = await browser.GetElement(By.XPath(node.XPath), cancellationToken);
            if (isFailed) return Result.Fail(errors);

            return await browser.Input(element, $"{amount}", cancellationToken);
        }

        private static async Task<Result> InputCoordinates(IChromeBrowser browser, int x, int y, CancellationToken cancellationToken)
        {
            var xNode = RallyPointSendTroopsParser.GetXInput(browser.Html);
            if (xNode is null) return Retry.Error.WithError("Cannot find X coordinate input.");

            var (_, xFailed, xElement, xErrors) = await browser.GetElement(By.XPath(xNode.XPath), cancellationToken);
            if (xFailed) return Result.Fail(xErrors);

            var result = await browser.Input(xElement, $"{x}", cancellationToken);
            if (result.IsFailed) return result;

            var yNode = RallyPointSendTroopsParser.GetYInput(browser.Html);
            if (yNode is null) return Retry.Error.WithError("Cannot find Y coordinate input.");

            var (_, yFailed, yElement, yErrors) = await browser.GetElement(By.XPath(yNode.XPath), cancellationToken);
            if (yFailed) return Result.Fail(yErrors);

            return await browser.Input(yElement, $"{y}", cancellationToken);
        }

        private static async Task<Result> SelectReinforcement(IChromeBrowser browser, CancellationToken cancellationToken)
        {
            var node = RallyPointSendTroopsParser.GetEventTypeRadio(browser.Html, ReinforcementEventType);
            if (node is null) return Retry.Error.WithError("Cannot find the 'Reinforcement' movement type option.");

            var (_, isFailed, element, errors) = await browser.GetElement(By.XPath(node.XPath), cancellationToken);
            if (isFailed) return Result.Fail(errors);

            return await browser.Click(element, cancellationToken);
        }

        private static async Task<Result> ClickSend(IChromeBrowser browser, CancellationToken cancellationToken)
        {
            var node = RallyPointSendTroopsParser.GetSendButton(browser.Html);
            if (node is null) return Retry.Error.WithError("Cannot find the send button.");

            var (_, isFailed, element, errors) = await browser.GetElement(By.XPath(node.XPath), cancellationToken);
            if (isFailed) return Result.Fail(errors);

            return await browser.Click(element, cancellationToken);
        }

        private static async Task<Result> ClickConfirm(IChromeBrowser browser, CancellationToken cancellationToken)
        {
            var node = RallyPointSendTroopsParser.GetConfirmButton(browser.Html);
            if (node is null) return Retry.Error.WithError("Cannot find the confirm button.");

            var (_, isFailed, element, errors) = await browser.GetElement(By.XPath(node.XPath), cancellationToken);
            if (isFailed) return Result.Fail(errors);

            return await browser.Click(element, cancellationToken);
        }
    }
}
