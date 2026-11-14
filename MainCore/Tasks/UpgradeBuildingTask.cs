using MainCore.Commands.Features.Scheduling;
using MainCore.Commands.Features.UpgradeBuilding;
using MainCore.Tasks.Base;

namespace MainCore.Tasks
{
    [Handler]
    public static partial class UpgradeBuildingTask
    {
        public sealed class Task : VillageTask
        {
            public Task(AccountId accountId, VillageId villageId) : base(accountId, villageId)
            {
            }

            protected override string TaskName => "Upgrade building";
        }

        // 2026-11-07 (user decision): a building was just started - let the village's troop training look at what is left
        // 10-20 s from now instead of at its own (possibly hours away) time. Only moves the task earlier, never forces a
        // batch (intervals, busy queue, horizon and construction reserve still decide). See TrainKickRules.
        private static void KickTrainTroop(Task task, AppDbContext context, ITaskManager taskManager, ILogger logger)
        {
            try
            {
                if (!context.BooleanByName(task.VillageId, VillageSettingEnums.TrainTroopEnable)) return;
                // 2026-11-13: the share mode has no visit times to bring forward (it reacts to the stock itself).
                if (context.BooleanByName(task.VillageId, VillageSettingEnums.TrainShareEnable)) return;

                var current = taskManager.GetTaskList(task.AccountId)
                    .OfType<TrainTroopTask.Task>()
                    .FirstOrDefault(x => x.VillageId == task.VillageId);

                var kick = TrainKickRules.KickTime(DateTime.Now, current?.ExecuteAt, Random.Shared);
                if (kick is null) return;

                var kickTask = new TrainTroopTask.Task(task.AccountId, task.VillageId) { ExecuteAt = kick.Value };
                taskManager.AddOrUpdate<TrainTroopTask.Task>(kickTask);
                logger.Information("Upgrade building in {VillageId}: a building was started, troop training looks at the rest at {Time:HH:mm:ss}.", task.VillageId, kick.Value);
            }
            catch (Exception ex)
            {
                // A helper must never fail the build task.
                logger.Warning("Upgrade building in {VillageId}: could not bring the troop training forward ({Reason}).", task.VillageId, ex.Message);
            }
        }

        private static async ValueTask<Result> HandleAsync(
            Task task,
            ILogger logger,
            IChromeBrowser browser,
            AppDbContext context,
            ITaskManager taskManager,
            GetBuildPlanCommand.Handler getBuildPlanCommand,
            ToBuildPageCommand.Handler toBuildPageCommand,
            HandleResourceCommand.Handler handleResourceCommand,
            ResolveMissingPrerequisiteCommand.Handler resolveMissingPrerequisiteCommand,
            AddCroplandCommand.Handler addCroplandCommand,
            HandleUpgradeCommand.Handler handleUpgradeCommand,
            UpdateBuildingCommand.Handler updateBuildingCommand,
            CancellationToken cancellationToken)
        {
            Result result;

            // Safety net: how many times this single run may auto-queue a Warehouse/Granary upgrade
            // (one for each side is the realistic worst case) before falling back to the old Stop.
            const int maxStorageResolutions = 2;
            var storageResolutions = 0;
            const int maxCropResolutions = 2;
            var cropResolutions = 0;

            while (true)
            {
                if (cancellationToken.IsCancellationRequested) return Cancel.Error;

                var (_, isFailed, plan, errors) = await getBuildPlanCommand.HandleAsync(new(task.AccountId, task.VillageId), cancellationToken);
                if (isFailed)
                {
                    var nextExecuteErrors = errors.OfType<NextExecuteError>().OrderBy(x => x.NextExecute).ToList();
                    if (nextExecuteErrors.Count > 0)
                    {
                        task.ExecuteAt = nextExecuteErrors.Select(x => x.NextExecute).Min();
                    }

                    // 2026-11-07 (construction reserve): a full build queue keeps the plan's cost known and notes
                    // when the slot frees; any other reason (no jobs, ...) means nothing is waiting for resources.
                    var queueFull = nextExecuteErrors.Where(x => x.PrerequisiteLevel == 0).ToList();
                    if (queueFull.Count > 0)
                    {
                        ConstructionNeed.SetSlotBusyUntil(task.VillageId, queueFull.Min(x => x.NextExecute));
                    }
                    else
                    {
                        ConstructionNeed.Clear(task.VillageId);
                    }

                    // Nothing (buildable) is waiting for resources: troops may use them again.
                    ConstructionHold.Clear(task.VillageId);
                    return Skip.Error.WithErrors(errors);
                }

                logger.Information("Build {Type} to level {Level} at location {Location}", plan.Type, plan.Level, plan.Location);
                ConstructionNeed.ClearSlotBusy(task.VillageId);

                result = await toBuildPageCommand.HandleAsync(new(task.VillageId, plan), cancellationToken);
                if (result.IsFailed) return result;

                result = await handleResourceCommand.HandleAsync(new(task.AccountId, task.VillageId, plan), cancellationToken);
                if (result.IsFailed)
                {
                    if (result.HasError<LackOfFreeCrop>())
                    {
                        if (!await addCroplandCommand.HandleAsync(new(task.VillageId), cancellationToken))
                        {
                            task.ExecuteAt = DateTime.Now.AddHours(UpgradeWaitRules.NoCroplandSuspendHours);
                            logger.Warning("Upgrade building in {VillageId}: not enough free crop and no cropland left to upgrade - building tasks suspended until {Time:HH:mm}.", task.VillageId, task.ExecuteAt);
                            return Skip.Error.WithErrors(result.Errors);
                        }
                        continue;
                    }

                    if (result.HasError<StorageLimit>())
                    {
                        // The building costs more of a resource than the warehouse/granary can ever hold,
                        // so waiting never helps. Instead of pausing the whole account, put the missing
                        // storage upgrade at the top of the build queue (same mechanism as missing
                        // prerequisites) and loop - GetBuildPlanCommand will pick it up next.
                        var storageError = result.Errors.OfType<StorageLimit>().First();
                        if (storageResolutions < maxStorageResolutions)
                        {
                            storageResolutions++;
                            var queued = await resolveMissingPrerequisiteCommand.HandleAsync(new(task.VillageId, storageError.Building, 0), cancellationToken);
                            if (queued)
                            {
                                logger.Information("{Building} is too small for {Type} level {Level}, queued a {Building} upgrade first.", storageError.Building, plan.Type, plan.Level, storageError.Building);
                                continue;
                            }
                        }

                        return Stop.Error.WithErrors(result.Errors);
                    }
                    if (result.HasError<MissingResource>())
                    {
                        var time = UpgradeParser.GetTimeWhenEnoughResource(browser.Html, plan.Type);
                        var blockedMessage = UpgradeParser.GetBlockedMessage(browser.Html, plan.Type);
                        var cropBlocked = UpgradeWaitRules.IsCropProductionBlocked(blockedMessage);
                        // 2026-10-27: crop production is negative - queue a cropland upgrade as the FIRST job
                        // (any other plan, e.g. Grain Mill, can never be built before crop production rises).
                        if (cropBlocked && plan.Type != BuildingEnums.Cropland)
                        {
                            if (cropResolutions < maxCropResolutions)
                            {
                                cropResolutions++;
                                if (await addCroplandCommand.HandleAsync(new(task.VillageId), cancellationToken))
                                {
                                    logger.Information("Crop production is too low for {Type} level {Level}: queued a cropland upgrade as the first job.", plan.Type, plan.Level);
                                    continue;
                                }
                            }

                            // No cropland left to upgrade: suspend this village's building tasks for a while.
                            task.ExecuteAt = DateTime.Now.AddHours(UpgradeWaitRules.NoCroplandSuspendHours);
                            logger.Warning("Upgrade building in {VillageId}: crop production is too low for {Type} level {Level} and there is no cropland left to upgrade - building tasks suspended until {Time:HH:mm}.", task.VillageId, plan.Type, plan.Level, task.ExecuteAt);
                            return Skip.Error.WithErrors(result.Errors);
                        }

                        // 2026-10-26: the page shows no countdown when crop production is too low; the old code then
                        // read 0 s and retried every ~16 s (678 runs in 3 h). See UpgradeWaitRules.
                        task.ExecuteAt = DateTime.Now.Add(UpgradeWaitRules.NextWait(time, cropBlocked, Random.Shared));
                        if (time <= TimeSpan.Zero)
                        {
                            logger.Warning(
                                cropBlocked
                                    ? "Upgrade building in {VillageId}: {Type} level {Level} is blocked by the game (\"{Message}\") - crop production is too low, nothing can be built here until it rises. Looking again at {Time:HH:mm}."
                                    : "Upgrade building in {VillageId}: could not read the time until enough resources (page says \"{Message}\"). Looking again at {Time:HH:mm}.",
                                task.VillageId, plan.Type, plan.Level, blockedMessage, task.ExecuteAt);
                        }

                        // "Construction first" village setting: troops of this village wait until then - not for a
                        // crop-blocked plan, which resources cannot unblock.
                        if (!cropBlocked) ConstructionHold.Set(task.VillageId, task.ExecuteAt);
                        return Skip.Error.WithErrors(result.Errors);
                    }

                    return result;
                }

                result = await handleUpgradeCommand.HandleAsync(new(task.VillageId, plan), cancellationToken);
                if (result.IsFailed) return result;

                logger.Information("Upgrade for {Type} at location {Location} completed successfully.", plan.Type, plan.Location);
                ConstructionHold.Clear(task.VillageId);
                KickTrainTroop(task, context, taskManager, logger);

                // The cost of the plan just built stays as the best estimate for the next one (the build queue is busy
                // now); HandleResourceCommand replaces it as soon as the next plan is read, the no-jobs path clears it.

                result = await updateBuildingCommand.HandleAsync(new(task.VillageId), cancellationToken);
                if (result.IsFailed) return result;
            }
        }
    }
}