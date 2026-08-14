namespace MainCore.Commands.Features.Settle
{
    // Trains the given amount of the tribe's settler/chieftain on the Residence/Palace
    // "Train" tab (reached via ToSettlerTrainPageCommand). Deliberately its own small command
    // rather than reusing the generic TrainTroopCommand flow, since settlers aren't picked via
    // a VillageSettingEnums-configured slot like Barracks/Stable troops are - the tribe's
    // settler troop is always slot 10 (see AutoSettleTask).
    [Handler]
    public static partial class TrainSettlerCommand
    {
        public sealed record Command(VillageId VillageId, TroopEnums SettlerTroop, int Amount) : IVillageCommand;

        private static async ValueTask<Result> HandleAsync(
            Command command,
            IChromeBrowser browser,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            var (villageId, settlerTroop, amount) = command;

            var (_, inputFailed, inputElement, inputErrors) = await browser.GetElement(doc => TrainTroopParser.GetInputBox(doc, settlerTroop), cancellationToken);
            if (inputFailed) return Result.Fail(inputErrors);

            var result = await browser.Input(inputElement, $"{amount}", cancellationToken);
            if (result.IsFailed) return result;

            var (_, buttonFailed, buttonElement, buttonErrors) = await browser.GetElement(doc => TrainTroopParser.GetTrainButton(doc), cancellationToken);
            if (buttonFailed) return Result.Fail(buttonErrors);

            result = await browser.Click(buttonElement, cancellationToken);
            if (result.IsFailed) return result;

            logger.Information("Training {Amount} settler(s) in village {VillageId}.", amount, villageId);

            return Result.Ok();
        }
    }
}
