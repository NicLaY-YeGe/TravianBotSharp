namespace MainCore.Commands.Features.SmithyUpgrade
{
    [Handler]
    public static partial class ToSmithyPageCommand
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

            // Smithy is a single page, no sub-tabs to switch between.
            result = await toBuildingCommand.HandleAsync(new(villageId, BuildingEnums.Smithy), cancellationToken);
            if (result.IsFailed) return result;

            return Result.Ok();
        }
    }
}
