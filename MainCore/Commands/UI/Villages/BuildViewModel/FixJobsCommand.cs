using System.Text.Json;
using MainCore.Commands.Features.UpgradeBuilding;

namespace MainCore.Commands.UI.Villages.BuildViewModel
{
    [Handler]
    public static partial class FixJobsCommand
    {
        public sealed record Command(VillageId VillageId, List<JobDto> Jobs, bool Shuffle) : IVillageCommand;

        private static async ValueTask<List<JobDto>> HandleAsync(
            Command command,
            GetLayoutBuildingsCommand.Handler getLayoutBuildingsQuery,
            ILogger logger
            )
        {
            var (villageId, jobs, shuffle) = command;
            var buildings = await getLayoutBuildingsQuery.HandleAsync(new(villageId));

            // 2026-11-19: resource-field plans are paired with this village's fields by TYPE and ordinal instead of being
            // dropped when the slot of the list holds another resource (see ResourceFieldMapRules).
            var fieldMap = BuildFieldMap(buildings, jobs);
            LogFieldMap(logger, villageId, fieldMap);

            var modifiedJobs = GetModifiedJobs(buildings, jobs, shuffle, fieldMap);
            return [.. modifiedJobs];
        }

        private static Dictionary<(int Type, int Location), int> BuildFieldMap(List<BuildingItem> buildings, List<JobDto> jobs)
        {
            var listUses = new List<(int Type, int Location)>();
            foreach (var job in jobs)
            {
                if (job.Type != JobTypeEnums.NormalBuild) continue;
                NormalBuildPlan? plan;
                try
                {
                    plan = JsonSerializer.Deserialize<NormalBuildPlan>(job.Content);
                }
                catch (JsonException)
                {
                    continue;
                }
                if (plan is null || !plan.Type.IsResourceField()) continue;
                listUses.Add(((int)plan.Type, plan.Location));
            }

            var villageFields = buildings
                .Where(x => x.Type.IsResourceField())
                .Select(x => (x.Location, (int)x.Type));
            return ResourceFieldMapRules.Build(listUses, villageFields);
        }

        private static void LogFieldMap(ILogger logger, VillageId villageId, Dictionary<(int Type, int Location), int> fieldMap)
        {
            var moved = fieldMap.Where(x => x.Value != ResourceFieldMapRules.NoField && x.Value != x.Key.Location).ToList();
            if (moved.Count > 0)
            {
                logger.Information(
                    "Build list for village {VillageId}: resource fields of the list were paired with this village's fields by type ({Pairs}).",
                    villageId, string.Join(", ", moved.OrderBy(x => x.Key.Type).ThenBy(x => x.Key.Location).Select(x => $"{(BuildingEnums)x.Key.Type} {x.Key.Location} -> {x.Value}")));
            }

            var dropped = fieldMap.Where(x => x.Value == ResourceFieldMapRules.NoField).ToList();
            if (dropped.Count > 0)
            {
                logger.Warning(
                    "Build list for village {VillageId}: the village has fewer fields of some types than the list uses - jobs for {Fields} were left out.",
                    villageId, string.Join(", ", dropped.OrderBy(x => x.Key.Type).ThenBy(x => x.Key.Location).Select(x => $"{(BuildingEnums)x.Key.Type} (list location {x.Key.Location})")));
            }
        }

        private static readonly List<int> _excludedLocations = new() { 26, 39, 40 }; //main building, rallypoint and wall

        private static readonly Dictionary<ResourcePlanEnums, List<BuildingEnums>> _fieldList = new()
        {
            {ResourcePlanEnums.AllResources, new(){
                BuildingEnums.Woodcutter,
                BuildingEnums.ClayPit,
                BuildingEnums.IronMine,
                BuildingEnums.Cropland,}},
            {ResourcePlanEnums.ExcludeCrop, new() {
                BuildingEnums.Woodcutter,
                BuildingEnums.ClayPit,
                BuildingEnums.IronMine,}},
            {ResourcePlanEnums.OnlyCrop, new() {
                BuildingEnums.Cropland,}},
        };

        private static IEnumerable<JobDto> GetModifiedJobs(List<BuildingItem> buildings, List<JobDto> jobs, bool shuffle, Dictionary<(int Type, int Location), int> fieldMap)
        {
            var changedLocations = new Dictionary<int, int>();
            foreach (var job in jobs)
            {
                switch (job.Type)
                {
                    case JobTypeEnums.NormalBuild:
                        {
                            var plan = JsonSerializer.Deserialize<NormalBuildPlan>(job.Content)!;

                            if (plan.Type.IsResourceField())
                            {
                                // -1 (no counterpart) finds no building in IsValidPlan, so the job is left out.
                                plan.Location = ResourceFieldMapRules.Resolve(fieldMap, (int)plan.Type, plan.Location);
                            }
                            else
                            {
                                Modify(buildings, plan, changedLocations, shuffle);
                            }
                            job.Content = GetContent(plan);

                            if (IsValidPlan(buildings, plan)) yield return job;
                            continue;
                        }
                    case JobTypeEnums.ResourceBuild:
                        {
                            var plan = JsonSerializer.Deserialize<ResourceBuildPlan>(job.Content)!;
                            if (IsValidPlan(buildings, plan)) yield return job;
                            continue;
                        }
                    default:
                        continue;
                }
            }
        }

        private static string GetContent(NormalBuildPlan plan)
        {
            return JsonSerializer.Serialize(plan);
        }

        private static bool IsValidPlan(List<BuildingItem> buildings, ResourceBuildPlan plan)
        {
            var fieldTypes = _fieldList[plan.Plan];

            var fields = buildings
                .Where(x => fieldTypes.Contains(x.Type))
                .ToList();

            if (fields.TrueForAll(x => x.Level >= plan.Level)) return false;

            foreach (var field in fields)
            {
                if (field.Level < plan.Level) field.JobLevel = plan.Level;
            }
            return true;
        }

        private static bool IsValidPlan(List<BuildingItem> buildings, NormalBuildPlan plan)
        {
            var building = buildings.Find(x => x.Location == plan.Location);
            if (building is null) return false;

            if (building.Type != BuildingEnums.Site)
            {
                if (building.Type != plan.Type) return false;
                if (building.Level >= plan.Level) return false;
            }
            else
            {
                building.Type = plan.Type;
            }

            building.JobLevel = plan.Level;
            return true;
        }

        private static void Modify(List<BuildingItem> buildings, NormalBuildPlan plan, Dictionary<int, int> changedLocations, bool shuffle)
        {
            if (plan.Type.IsResourceField()) return;

            if (plan.Type.IsMultipleBuilding())
            {
                if (ModifyMultiple(buildings, plan)) return;
            }
            else
            {
                if (plan.Type.IsWall())
                {
                    // Location 40 should always be scanned before this runs (see
                    // ApplyBuildTemplateTask.CanStart), but fall back to the plan's own type
                    // rather than crash if it isn't there yet.
                    var wall = buildings.Find(x => x.Location == 40);
                    if (wall is not null && plan.Type != wall.Type)
                    {
                        plan.Type = wall.Type;
                    }
                }

                if (ModifySame(buildings, plan)) return;
            }

            if (_excludedLocations.Contains(plan.Location)) return;

            if (shuffle)
            {
                ModifyRandom(buildings, plan, changedLocations);
            }
        }

        private static bool ModifyMultiple(List<BuildingItem> buildings, NormalBuildPlan plan)
        {
            var largestLevelBuilding = buildings
                .Where(x => x.Type == plan.Type)
                .OrderByDescending(x => x.Level)
                .FirstOrDefault();
            if (largestLevelBuilding is not null && largestLevelBuilding.Type.GetMaxLevel() > largestLevelBuilding.Level)
            {
                plan.Location = largestLevelBuilding.Location;
                return true;
            }
            return false;
        }

        private static bool ModifySame(List<BuildingItem> buildings, NormalBuildPlan plan)
        {
            var sameTypeBuilding = buildings
                .Find(x => x.Type == plan.Type);
            if (sameTypeBuilding is not null)
            {
                if (sameTypeBuilding.Location != plan.Location)
                {
                    plan.Location = sameTypeBuilding.Location;
                }
                return true;
            }
            return false;
        }

        private static void ModifyRandom(List<BuildingItem> buildings, NormalBuildPlan plan, Dictionary<int, int> changedLocations)
        {
            var freeLocations = buildings
              .Where(x => x.Type == BuildingEnums.Site)
              .Select(x => x.Location)
              .Where(x => !_excludedLocations.Contains(x))
              .ToList();

            if (freeLocations.Count == 0)
            {
                plan.Location = -1;
            }
            else
            {
                if (!changedLocations.TryGetValue(plan.Location, out int value))
                {
                    value = GetRandomLocation(freeLocations);
                    changedLocations[plan.Location] = value;
                }

                plan.Location = value;
            }
        }

        private static int GetRandomLocation(List<int> freeLocations)
        {
            return freeLocations[Random.Shared.Next(0, freeLocations.Count - 1)];
        }
    }
}