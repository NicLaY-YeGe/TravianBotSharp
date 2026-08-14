namespace MainCore.Commands.Features.Settle
{
    // Navigates to the Residence/Palace "Expansion" tab - not strictly required before
    // FoundNewVillageCommand (which navigates to the Map page itself), but lets the UI/task
    // land somewhere sensible and doubles as a building-presence check before attempting to
    // found. See ToSettlerTrainPageCommand for the verified tab order.
    [Handler]
    public static partial class ToExpansionPageCommand
    {
        public sealed record Command(VillageId VillageId, BuildingEnums Building) : IVillageCommand;

        public const int ExpansionTabIndex = 4;

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

            result = await switchTabCommand.HandleAsync(new(ExpansionTabIndex), cancellationToken);
            if (result.IsFailed) return result;

            return Result.Ok();
        }
    }
}
