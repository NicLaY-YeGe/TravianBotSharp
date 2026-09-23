using MainCore.Commands.Features.Trap;
using MainCore.Commands.NextExecute;
using MainCore.Tasks.Base;

namespace MainCore.Tasks
{
    [Handler]
    public static partial class TrapTask
    {
        public sealed class Task : VillageTask
        {
            public Task(AccountId accountId, VillageId villageId) : base(accountId, villageId)
            {
            }

            protected override string TaskName => "Build traps";

            public override bool CanStart(AppDbContext context)
            {
                return context.BooleanByName(VillageId, VillageSettingEnums.TrapEnable);
            }
        }

        private static async ValueTask<Result> HandleAsync(
            Task task,
            ToTrapPageCommand.Handler toTrapPageCommand,
            BuildTrapsCommand.Handler buildTrapsCommand,
            NextExecuteTrapTaskCommand.Handler nextExecuteTrapTaskCommand,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            var result = await toTrapPageCommand.HandleAsync(new(task.VillageId), cancellationToken);
            if (result.IsFailed)
            {
                if (result.HasError<MissingBuilding>())
                {
                    // No Trapper in this village (Trapper is Gaul-only, or it just hasn't been
                    // built yet) - nothing to retry right now. Quietly skip this cycle rather
                    // than failing the whole task; if a Trapper appears later (tribe/building
                    // change) the next cycle will pick it up on its own.
                    logger.Warning("No Trapper here, skipping trap build this cycle.");
                    await nextExecuteTrapTaskCommand.HandleAsync(new(task), cancellationToken);
                    return Result.Ok();
                }
                return result;
            }

            result = await buildTrapsCommand.HandleAsync(new(task.VillageId), cancellationToken);
            if (result.IsFailed && !result.HasError<MissingResource>())
            {
                return result;
            }
            // A MissingResource here (per BuildTrapsCommand: either genuinely 0 affordable, or
            // TrainWhenLowResource is off and the target batch doesn't fit) is not a task
            // failure - same philosophy as TrainTroopTask - just try again next cycle.

            await nextExecuteTrapTaskCommand.HandleAsync(new(task), cancellationToken);
            return Result.Ok();
        }
    }
}
