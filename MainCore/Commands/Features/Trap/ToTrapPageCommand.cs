namespace MainCore.Commands.Features.Trap
{
    // Same shape as ToTrainTroopPageCommand, hardcoded to BuildingEnums.Trapper since (unlike
    // troop training, which can live in any of several buildings) there is exactly one place
    // traps get built.
    [Handler]
    public static partial class ToTrapPageCommand
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

            var (_, isFailed, errors) = await updateBuildingCommand.HandleAsync(new(villageId), cancellationToken);
            if (isFailed) return Result.Fail(errors);

            // Returns MissingBuilding when this village has no Trapper (wrong tribe - Trapper
            // is Gaul-only, per the building's own description on its page - or just not built
            // yet); TrapTask treats that as a quiet no-op, not a task failure.
            result = await toBuildingCommand.HandleAsync(new(villageId, BuildingEnums.Trapper), cancellationToken);
            if (result.IsFailed) return result;

            return Result.Ok();
        }
    }
}
