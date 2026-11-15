using MainCore.Commands.Features.UpgradeBuilding;
using MainCore.Commands.NextExecute;
using MainCore.Tasks.Base;

namespace MainCore.Tasks
{
    [Handler]
    public static partial class UpdateVillageTask
    {
        public sealed class Task : VillageTask
        {
            public Task(AccountId accountId, VillageId villageId) : base(accountId, villageId)
            {
            }

            protected override string TaskName => "Update village";

            public override bool CanStart(AppDbContext context)
            {
                var settingEnable = context.BooleanByName(VillageId, VillageSettingEnums.AutoRefreshEnable);
                if (!settingEnable) return false;

                return true;
            }
        }

        private static async ValueTask<Result> HandleAsync(
            Task task,
            IChromeBrowser browser,
            UpdateBuildingCommand.Handler updateBuildingCommand,
            ToDorfCommand.Handler toDorfCommand,
            NextExecuteUpdateVillageTaskCommand.Handler nextExecuteUpdateVillageTaskCommand,
            EnsureEssentialBuildingsCommand.Handler ensureEssentialBuildingsCommand,
            ITaskManager taskManager,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            var url = browser.CurrentUrl;
            Result result;

            bool isFailed;
            IReadOnlyList<IError> errors;

            if (url.Contains("dorf1"))
            {
                (_, isFailed, errors) = await updateBuildingCommand.HandleAsync(new(task.VillageId), cancellationToken);
                if (isFailed) return Result.Fail(errors);
            }
            else if (url.Contains("dorf2"))
            {
                (_, isFailed, errors) = await updateBuildingCommand.HandleAsync(new(task.VillageId), cancellationToken);
                if (isFailed) return Result.Fail(errors);

                result = await toDorfCommand.HandleAsync(new(1), cancellationToken);
                if (result.IsFailed) return result;

                (_, isFailed, errors) = await updateBuildingCommand.HandleAsync(new(task.VillageId), cancellationToken);
                if (isFailed) return Result.Fail(errors);
            }
            else
            {
                result = await toDorfCommand.HandleAsync(new(1), cancellationToken);
                if (result.IsFailed) return result;

                (_, isFailed, errors) = await updateBuildingCommand.HandleAsync(new(task.VillageId), cancellationToken);
                if (isFailed) return Result.Fail(errors);
            }

            // 2026-11-04: look for a destroyed Main Building / Warehouse / Granary on every refresh and queue its rebuild
            // (level 3). Never fails this task: a problem here is only logged (CLAUDE.md rule 4), and a user pause passes.
            var essential = await ensureEssentialBuildingsCommand.HandleAsync(new(task.VillageId), cancellationToken);
            if (essential.HasError<Cancel>()) return Cancel.Error;
            if (essential.IsFailed)
            {
                logger.Warning("Update village in {VillageId}: the check for the Main Building / Warehouse / Granary did not finish ({Reason}).",
                    task.VillageId, string.Join(" | ", essential.Errors.Select(x => x.Message)));
            }
            else if (essential.Value > 0)
            {
                // Run Upgrade building now so the rebuild starts at once (it was sitting behind a waiting plan or not queued at all).
                taskManager.AddOrUpdate<UpgradeBuildingTask.Task>(new(task.AccountId, task.VillageId));
            }

            await nextExecuteUpdateVillageTaskCommand.HandleAsync(new(task), cancellationToken);
            return Result.Ok();
        }
    }
}