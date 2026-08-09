using System.Text.Json;

namespace MainCore.Commands.Features.UpgradeBuilding
{
    [Handler]
    public static partial class GetBuildPlanCommand
    {
        public sealed record Command(AccountId AccountId, VillageId VillageId) : IAccountVillageCommand;

        private static async ValueTask<Result<NormalBuildPlan>> HandleAsync(
            Command command,
            AppDbContext context,
            GetJobCommand.Handler getJobQuery,
            ToDorfCommand.Handler toDorfCommand,
            UpdateBuildingCommand.Handler updateBuildingCommand,
            GetLayoutBuildingsCommand.Handler getLayoutBuildingsQuery,
            DeleteJobByIdCommand.Handler deleteJobByIdCommand,
            AddJobCommand.Handler addJobCommand,
            ValidatePlanCompleteCommand.Handler validatePlanCompleteCommand,
            ResolveMissingPrerequisiteCommand.Handler resolveMissingPrerequisiteCommand,
            ILogger logger,
            IRxQueue rxQueue,
            CancellationToken cancellationToken
        )
        {
            var (accountId, villageId) = command;

            // Safety net for auto-resolving missing prerequisites (see the ValidatePlanCompleteCommand
            // failure branch below): caps how many prerequisite jobs this single call will chain through,
            // so a bug in the prerequisite table (or a village stuck with no empty plots) can't turn into
            // an infinite loop.
            const int maxPrerequisiteResolutions = 8;
            var prerequisiteResolutions = 0;

            while (true)
            {
                if (cancellationToken.IsCancellationRequested) return Cancel.Error;

                var (_, isFailed, job, errors) = await getJobQuery.HandleAsync(new(accountId, villageId), cancellationToken);
                if (isFailed) return Result.Fail(errors);

                if (job.Type == JobTypeEnums.ResourceBuild)
                {
                    logger.Information("{Content}", job);

                    var layoutBuildings = await getLayoutBuildingsQuery.HandleAsync(new(villageId, true));
                    var resourceBuildPlan = JsonSerializer.Deserialize<ResourceBuildPlan>(job.Content)!;

                    var storage = resourceBuildPlan.PriorityLowestStock
                        ? context.Storages.AsNoTracking().FirstOrDefault(x => x.VillageId == villageId.Value)
                        : null;

                    var normalBuildPlan = GetNormalBuildPlan(resourceBuildPlan, layoutBuildings, storage);
                    if (normalBuildPlan is null)
                    {
                        await deleteJobByIdCommand.HandleAsync(new(job.Id), cancellationToken);
                    }
                    else
                    {
                        await addJobCommand.HandleAsync(new(villageId, normalBuildPlan.ToJob(), true));
                    }
                    rxQueue.Enqueue(new JobsModified(villageId));
                    continue;
                }

                var plan = JsonSerializer.Deserialize<NormalBuildPlan>(job.Content)!;
                Result result;
                if (plan.Type.IsResourceBonus())
                {
                    result = await toDorfCommand.HandleAsync(new(1), cancellationToken);
                    if (result.IsFailed) return result;

                    result = await updateBuildingCommand.HandleAsync(new(villageId), cancellationToken);
                    if (result.IsFailed) return result;

                    result = await toDorfCommand.HandleAsync(new(2), cancellationToken);
                    if (result.IsFailed) return result;

                    result = await updateBuildingCommand.HandleAsync(new(villageId), cancellationToken);
                    if (result.IsFailed) return result;
                }
                else
                {
                    var dorf = plan.Location < 19 ? 1 : 2;
                    result = await toDorfCommand.HandleAsync(new(dorf), cancellationToken);
                    if (result.IsFailed) return result;

                    result = await updateBuildingCommand.HandleAsync(new(villageId), cancellationToken);
                    if (result.IsFailed) return result;
                }

                var validateResult = await validatePlanCompleteCommand.HandleAsync(new(villageId, plan), cancellationToken);
                if (validateResult.IsFailed)
                {
                    var missingPrerequisite = validateResult.Errors
                        .OfType<UpgradeBuildingError>()
                        .FirstOrDefault(x => x.PrerequisiteLevel > 0);

                    // Already queued in-game (NextExecuteError.PrerequisiteBuildingInQueue for this
                    // specific type+level) - nothing to auto-resolve, just wait for it like before.
                    var alreadyQueued = missingPrerequisite is not null && validateResult.Errors
                        .OfType<NextExecuteError>()
                        .Any(x => x.PrerequisiteType == missingPrerequisite.PrerequisiteType && x.PrerequisiteLevel == missingPrerequisite.PrerequisiteLevel);

                    if (missingPrerequisite is not null && !alreadyQueued)
                    {
                        if (prerequisiteResolutions >= maxPrerequisiteResolutions)
                        {
                            logger.Warning("Prerequisite auto-resolve limit ({Max}) reached in {VillageId}, giving up for this cycle.", maxPrerequisiteResolutions, villageId);
                        }
                        else
                        {
                            prerequisiteResolutions++;
                            var resolved = await resolveMissingPrerequisiteCommand.HandleAsync(
                                new(villageId, missingPrerequisite.PrerequisiteType, missingPrerequisite.PrerequisiteLevel),
                                cancellationToken);
                            if (resolved) continue;
                        }
                    }

                    return Result.Fail(validateResult.Errors);
                }
                if (!validateResult.Value)
                {
                    await deleteJobByIdCommand.HandleAsync(new(job.Id), cancellationToken);
                    rxQueue.Enqueue(new JobsModified(villageId));
                    continue;
                }

                return plan;
            }
        }

        private static NormalBuildPlan? GetNormalBuildPlan(
            ResourceBuildPlan plan,
            List<BuildingItem> layoutBuildings,
            Storage? storage
        )
        {
            List<BuildingItem> resourceFields;

            if (plan.Plan == ResourcePlanEnums.ExcludeCrop)
            {
                resourceFields = layoutBuildings
                    .Where(x => x.Type == BuildingEnums.Woodcutter || x.Type == BuildingEnums.ClayPit || x.Type == BuildingEnums.IronMine)
                    .Where(x => x.Level < plan.Level)
                    .ToList();
            }
            else if (plan.Plan == ResourcePlanEnums.OnlyCrop)
            {
                resourceFields = layoutBuildings
                    .Where(x => x.Type == BuildingEnums.Cropland)
                    .Where(x => x.Level < plan.Level)
                    .ToList();
            }
            else
            {
                resourceFields = layoutBuildings
                    .Where(x => x.Type.IsResourceField())
                    .Where(x => x.Level < plan.Level)
                    .ToList();
            }

            if (resourceFields.Count == 0) return null;

            List<BuildingItem> candidates;

            if (plan.PriorityLowestStock && storage is not null)
            {
                // among the eligible field types, find the resource that's currently
                // sitting lowest in the village's warehouse/granary, and only upgrade
                // fields of that type (still picking the cheapest/lowest level one).
                var scarcestType = resourceFields
                    .Select(x => x.Type)
                    .Distinct()
                    .OrderBy(x => GetStock(storage, x))
                    .First();

                candidates = resourceFields.Where(x => x.Type == scarcestType).ToList();
            }
            else
            {
                candidates = resourceFields;
            }

            var minLevel = candidates
                .Select(x => x.Level)
                .Min();

            var chosenOne = candidates
                .Where(x => x.Level == minLevel)
                .OrderBy(x => x.Id.Value + Random.Shared.Next())
                .FirstOrDefault();

            if (chosenOne is null) return null;

            var normalBuildPlan = new NormalBuildPlan()
            {
                Type = chosenOne.Type,
                Level = chosenOne.Level + 1,
                Location = chosenOne.Location,
            };
            return normalBuildPlan;
        }

        private static long GetStock(Storage storage, BuildingEnums type) => type switch
        {
            BuildingEnums.Woodcutter => storage.Wood,
            BuildingEnums.ClayPit => storage.Clay,
            BuildingEnums.IronMine => storage.Iron,
            BuildingEnums.Cropland => storage.Crop,
            _ => long.MaxValue,
        };
    }
}