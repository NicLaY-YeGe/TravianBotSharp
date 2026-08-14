namespace MainCore.Commands.Features.Settle
{
    // Navigates to the Residence/Palace "Train" tab (settlers/chieftains). Tab order verified
    // against a real page capture (2026-08-12): 0 = Management, 1 = Train, 2 = Culture points,
    // 3 = Loyalty, 4 = Expansion - see ToExpansionPageCommand for the last one.
    [Handler]
    public static partial class ToSettlerTrainPageCommand
    {
        public sealed record Command(VillageId VillageId, BuildingEnums Building) : IVillageCommand;

        public const int TrainTabIndex = 1;

        private static async ValueTask<Result> HandleAsync(
            Command command,
            ToDorfCommand.Handler toDorfCommand,
            UpdateBuildingCommand.Handler updateBuildingCommand,
            ToBuildingByTypeCommand.Handler toBuildingCommand,
            SwitchTabCommand.Handler switchTabCommand,
            CancellationToken cancellationToken)
        {
            var (villageId, building) = command;

            var result = await toDorfCommand.HandleAsync(new(2), cancellationToken);
            if (result.IsFailed) return result;

            var (_, isFailed, errors) = await updateBuildingCommand.HandleAsync(new(villageId), cancellationToken);
            if (isFailed) return Result.Fail(errors);

            result = await toBuildingCommand.HandleAsync(new(villageId, building), cancellationToken);
            if (result.IsFailed) return result;

            result = await switchTabCommand.HandleAsync(new(TrainTabIndex), cancellationToken);
            if (result.IsFailed) return result;

            return Result.Ok();
        }
    }
}
