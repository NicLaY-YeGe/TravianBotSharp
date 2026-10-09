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

        private static async ValueTask<Result> HandleAsync(
            Task task,
            ILogger logger,
            IChromeBrowser browser,
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

                    return Skip.Error.WithErrors(errors);
                }

                logger.Information("Build {Type} to level {Level} at location {Location}", plan.Type, plan.Level, plan.Location);

                result = await toBuildPageCommand.HandleAsync(new(task.VillageId, plan), cancellationToken);
                if (result.IsFailed) return result;

                result = await handleResourceCommand.HandleAsync(new(task.AccountId, task.VillageId, plan), cancellationToken);
                if (result.IsFailed)
                {
                    if (result.HasError<LackOfFreeCrop>())
                    {
                        await addCroplandCommand.HandleAsync(new(task.VillageId), cancellationToken);
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
                        task.ExecuteAt = DateTime.Now.Add(time);
                        return Skip.Error.WithErrors(result.Errors);
                    }

                    return result;
                }

                result = await handleUpgradeCommand.HandleAsync(new(task.VillageId, plan), cancellationToken);
                if (result.IsFailed) return result;

                logger.Information("Upgrade for {Type} at location {Location} completed successfully.", plan.Type, plan.Location);

                result = await updateBuildingCommand.HandleAsync(new(task.VillageId), cancellationToken);
                if (result.IsFailed) return result;
            }
        }
    }
}