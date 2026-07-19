namespace MainCore.Commands.Features.DodgeTroop
{
    [Handler]
    public static partial class ToRallyPointOverviewCommand
    {
        public sealed record Command(VillageId VillageId) : IVillageCommand;

        // Rally Point tab order verified from a real page capture:
        // 0 = Management, 1 = Overview, 2 = Send troops, 3 = Simulators, 4 = Farm List.
        private const int OverviewTabIndex = 1;

        private static async ValueTask<Result> HandleAsync(
            Command command,
            ToDorfCommand.Handler toDorfCommand,
            UpdateBuildingCommand.Handler updateBuildingCommand,
            ToBuildingByTypeCommand.Handler toBuildingCommand,
            SwitchTabCommand.Handler switchTabCommand,
            CancellationToken cancellationToken)
        {
            var villageId = command.VillageId;

            var result = await toDorfCommand.HandleAsync(new(2), cancellationToken);
            if (result.IsFailed) return result;

            result = await updateBuildingCommand.HandleAsync(new(villageId), cancellationToken);
            if (result.IsFailed) return result;

            result = await toBuildingCommand.HandleAsync(new(villageId, BuildingEnums.RallyPoint), cancellationToken);
            if (result.IsFailed) return result;

            result = await switchTabCommand.HandleAsync(new(OverviewTabIndex), cancellationToken);
            if (result.IsFailed) return result;

            return Result.Ok();
        }
    }
}
