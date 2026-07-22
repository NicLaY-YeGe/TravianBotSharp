namespace MainCore.Commands.Features.Demolish
{
    [Handler]
    public static partial class ToDemolishPageCommand
    {
        public sealed record Command(VillageId VillageId) : IVillageCommand;

        private static async ValueTask<Result> HandleAsync(
            Command command,
            ToDorfCommand.Handler toDorfCommand,
            UpdateBuildingCommand.Handler updateBuildingCommand,
            ToBuildingByTypeCommand.Handler toBuildingCommand,
            CancellationToken cancellationToken)
        {
            var villageId = command.VillageId;

            var result = await toDorfCommand.HandleAsync(new(2), cancellationToken);
            if (result.IsFailed) return result;

            result = await updateBuildingCommand.HandleAsync(new(villageId), cancellationToken);
            if (result.IsFailed) return result;

            // The demolish dropdown is on the Main Building's own page, no separate tab.
            result = await toBuildingCommand.HandleAsync(new(villageId, BuildingEnums.MainBuilding), cancellationToken);
            if (result.IsFailed) return result;

            return Result.Ok();
        }
    }
}
