using MainCore.Tasks.Base;

namespace MainCore.Tasks
{
    [Handler]
    public static partial class UpdateBuildingTask
    {
        public sealed class Task : VillageTask
        {
            public Task(AccountId accountId, VillageId villageId) : base(accountId, villageId)
            {
            }

            protected override string TaskName => "Update building";
        }

        private static async ValueTask<Result> HandleAsync(
            Task task,
            IChromeBrowser browser,
            UpdateBuildingCommand.Handler updateBuildingCommand,
            ToDorfCommand.Handler toDorfCommand,
            CancellationToken cancellationToken)
        {
            var url = browser.CurrentUrl;
            Result result;

            bool isFailed;
            IReadOnlyList<IError> errors;
            if (TravianUrl.IsDorf1(url))
            {
                (_, isFailed, errors) = await updateBuildingCommand.HandleAsync(new(task.VillageId), cancellationToken);
                if (isFailed) return Result.Fail(errors);

                result = await toDorfCommand.HandleAsync(new(2), cancellationToken);
                if (result.IsFailed) return result;

                (_, isFailed, errors) = await updateBuildingCommand.HandleAsync(new(task.VillageId), cancellationToken);
                if (isFailed) return Result.Fail(errors);
            }
            else if (TravianUrl.IsDorf2(url))
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
                result = await toDorfCommand.HandleAsync(new(2), cancellationToken);
                if (result.IsFailed) return result;

                (_, isFailed, errors) = await updateBuildingCommand.HandleAsync(new(task.VillageId), cancellationToken);
                if (isFailed) return Result.Fail(errors);

                result = await toDorfCommand.HandleAsync(new(1), cancellationToken);
                if (result.IsFailed) return result;

                (_, isFailed, errors) = await updateBuildingCommand.HandleAsync(new(task.VillageId), cancellationToken);
                if (isFailed) return Result.Fail(errors);
            }
            return Result.Ok();
        }
    }
}