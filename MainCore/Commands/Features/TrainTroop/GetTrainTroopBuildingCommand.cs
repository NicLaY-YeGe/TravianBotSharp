namespace MainCore.Commands.Features.TrainTroop
{
    [Handler]
    public static partial class GetTrainTroopBuildingCommand
    {
        public sealed record Command(VillageId VillageId) : IVillageCommand;

        private static readonly BuildingEnums[] TrainingBuildings =
        {
            BuildingEnums.Barracks,
            BuildingEnums.Stable,
            BuildingEnums.GreatBarracks,
            BuildingEnums.GreatStable,
            BuildingEnums.Workshop,
        };

        // A building is used when at least one troop is configured for it (per-troop slots, or the old
        // single-troop setting - see TrainTroopPlanner).
        private static async ValueTask<List<BuildingEnums>> HandleAsync(
            Command command,
            AppDbContext context)
        {
            await Task.CompletedTask;
            var villageId = command.VillageId;

            var settings = context.VillagesSetting
                .Where(x => x.VillageId == villageId.Value)
                .ToDictionary(x => x.Setting, x => x.Value);
            int Get(VillageSettingEnums key) => settings.GetValueOrDefault(key);

            var tribe = (TribeEnums)Get(VillageSettingEnums.Tribe);
            return TrainingBuildings
                .Where(building => TrainTroopPlanner.GetPlans(building, tribe, Get).Count > 0)
                .ToList();
        }
    }
}
