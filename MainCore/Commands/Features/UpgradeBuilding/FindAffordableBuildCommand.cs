using System.Text.Json;

namespace MainCore.Commands.Features.UpgradeBuilding
{
    // 2026-11-19: part of the flexible build order (see FlexibleBuildRules). Called by UpgradeBuildingTask when the FIRST job of the
    // build list has to wait long for its resources: returns the plan of the first job BEHIND it that the village can pay for right
    // now (own stock plus the hero inventory when hero resources are allowed for building), or a failed result when there is none.
    //
    // Reads the database only (the task has just refreshed the buildings while it looked at the first job) - no page visits. The cost
    // of a candidate is the ESTIMATE BuildingEnums.GetCost(next level); the real cost is read from the build page when the candidate
    // is built. A candidate must be valid exactly like a normal plan (ValidatePlanCompleteCommand: slot, prerequisites, not already
    // done); a candidate with a missing prerequisite is skipped - no prerequisite chain is started for a job that is out of order.
    // The jobs and their order are never changed here.
    [Handler]
    public static partial class FindAffordableBuildCommand
    {
        public sealed record Command(AccountId AccountId, VillageId VillageId) : IAccountVillageCommand;

        private static readonly List<HeroItemEnums> ResourceItemTypes =
        [
            HeroItemEnums.Wood,
            HeroItemEnums.Clay,
            HeroItemEnums.Iron,
            HeroItemEnums.Crop,
        ];

        private static async ValueTask<Result<NormalBuildPlan>> HandleAsync(
            Command command,
            AppDbContext context,
            GetLayoutBuildingsCommand.Handler getLayoutBuildingsQuery,
            ValidatePlanCompleteCommand.Handler validatePlanCompleteCommand,
            CancellationToken cancellationToken)
        {
            var (accountId, villageId) = command;

            var storage = context.Storages
                .AsNoTracking()
                .FirstOrDefault(x => x.VillageId == villageId.Value);
            if (storage is null) return Result.Fail("The village's storage has not been read yet.");

            long[] stock = [storage.Wood, storage.Clay, storage.Iron, storage.Crop];

            var useHero = context.BooleanByName(villageId, VillageSettingEnums.UseHeroResourceForBuilding);
            var hero = useHero ? GetHeroStock(context, accountId) : new long[4];

            // Layout without the build list's own levels: what the village really has (queue included).
            var layout = await getLayoutBuildingsQuery.HandleAsync(new(villageId, true));

            // Same jobs, same order as GetJobCommand; the first one is the job that is waiting - never a candidate.
            var jobs = context.Jobs
                .AsNoTracking()
                .Where(x => x.VillageId == villageId.Value)
                .Where(x => x.Type == JobTypeEnums.NormalBuild || x.Type == JobTypeEnums.ResourceBuild)
                .OrderBy(x => x.Position)
                .ToDto()
                .ToList();

            bool CanPay(BuildingEnums type, int level)
            {
                var cost = type.GetCost(level);
                return FlexibleBuildRules.Judge(cost, stock, storage.FreeCrop, storage.Warehouse, storage.Granary, hero, useHero)
                    == FlexibleBuildRules.Verdict.Affordable;
            }

            foreach (var job in jobs.Skip(1).Take(FlexibleBuildRules.MaxJobsExamined))
            {
                if (cancellationToken.IsCancellationRequested) return Result.Fail("Cancelled.");

                if (job.Type == JobTypeEnums.ResourceBuild)
                {
                    var resourcePlan = JsonSerializer.Deserialize<ResourceBuildPlan>(job.Content);
                    if (resourcePlan is null) continue;

                    var fields = layout
                        .Where(x => IsEligibleField(resourcePlan.Plan, x.Type))
                        .Where(x => x.Level < resourcePlan.Level)
                        .Select(x => (x.Location, (int)x.Type, x.Level))
                        .ToList();

                    var location = FlexibleBuildRules.PickField(fields, (type, level) => CanPay((BuildingEnums)type, level));
                    if (location < 0) continue;

                    var field = layout.First(x => x.Location == location);
                    return new NormalBuildPlan()
                    {
                        Type = field.Type,
                        Level = field.Level + 1,
                        Location = field.Location,
                    };
                }

                NormalBuildPlan? plan;
                try
                {
                    plan = JsonSerializer.Deserialize<NormalBuildPlan>(job.Content);
                }
                catch (JsonException)
                {
                    continue;
                }
                if (plan is null) continue;

                var building = layout.Find(x => x.Location == plan.Location);
                if (building is null) continue;
                var nextLevel = (building.Type == BuildingEnums.Site ? 0 : Math.Max(building.Level, 0)) + 1;
                if (nextLevel > plan.Level) continue;
                if (!CanPay(plan.Type, nextLevel)) continue;

                var validation = await validatePlanCompleteCommand.HandleAsync(new(villageId, plan), cancellationToken);
                if (validation.IsFailed) continue;   // prerequisite missing, slot holds another building, ...
                if (!validation.Value) continue;     // already done

                return plan;
            }

            return Result.Fail("No job behind the first one can be paid for right now.");
        }

        private static bool IsEligibleField(ResourcePlanEnums plan, BuildingEnums type) => plan switch
        {
            ResourcePlanEnums.ExcludeCrop => type is BuildingEnums.Woodcutter or BuildingEnums.ClayPit or BuildingEnums.IronMine,
            ResourcePlanEnums.OnlyCrop => type == BuildingEnums.Cropland,
            _ => type.IsResourceField(),
        };

        // Resource items of the hero's inventory (account-wide). Refreshed only when hero resources are used, so it can be a little
        // old; the real read happens when the candidate is built.
        private static long[] GetHeroStock(AppDbContext context, AccountId accountId)
        {
            var items = context.HeroItems
                .AsNoTracking()
                .Where(x => x.AccountId == accountId.Value)
                .Where(x => ResourceItemTypes.Contains(x.Type))
                .ToList();

            var stock = new long[4];
            for (var i = 0; i < 4; i++)
            {
                var type = ResourceItemTypes[i];
                stock[i] = items.Where(x => x.Type == type).Sum(x => (long)x.Amount);
            }
            return stock;
        }
    }
}
