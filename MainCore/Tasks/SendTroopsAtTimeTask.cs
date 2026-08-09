using MainCore.Commands.Features.DodgeTroop;
using MainCore.Commands.Features.SyncAttack;
using MainCore.Tasks.Base;

namespace MainCore.Tasks
{
    // One instance per source village in a synchronized attack/reinforcement, created (with its
    // precomputed ExecuteAt) by SyncAttackPlanTask. There's no CanStart gate here on purpose -
    // this task's very presence in the queue with the right ExecuteAt IS the schedule; the
    // standard TimerManager tick (and VillageTaskBehavior, which switches to the right village
    // automatically before HandleAsync runs) take care of the rest.
    //
    // Reminder: if this account's "Online hours" restriction (see CLAUDE.md §5b) excludes the
    // computed ExecuteAt's hour, TimerManager will hold this task until an allowed hour and the
    // synchronized arrival will be missed. Not handled specially here - it's an existing,
    // orthogonal setting the user configured deliberately.
    [Handler]
    public static partial class SendTroopsAtTimeTask
    {
        public sealed class Task : VillageTask
        {
            public int TargetX { get; }
            public int TargetY { get; }
            public RallyPointEventTypeEnums EventType { get; }
            public IReadOnlyDictionary<int, long> TroopAmounts { get; }

            public Task(
                AccountId accountId,
                VillageId villageId,
                int targetX,
                int targetY,
                RallyPointEventTypeEnums eventType,
                IReadOnlyDictionary<int, long> troopAmounts)
                : base(accountId, villageId)
            {
                TargetX = targetX;
                TargetY = targetY;
                EventType = eventType;
                TroopAmounts = troopAmounts;
            }

            protected override string TaskName => $"Synchronized send to ({TargetX}|{TargetY})";
        }

        private static async ValueTask<Result> HandleAsync(
            Task task,
            ToSendTroopsPageCommand.Handler toSendTroopsPageCommand,
            SendTroopsCommand.Handler sendTroopsCommand,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            var toPageResult = await toSendTroopsPageCommand.HandleAsync(new(task.VillageId), cancellationToken);
            if (toPageResult.IsFailed) return toPageResult;

            var sendResult = await sendTroopsCommand.HandleAsync(
                new(task.VillageId, task.TargetX, task.TargetY, task.EventType, task.TroopAmounts, Confirm: true),
                cancellationToken);
            if (sendResult.IsFailed) return Result.Fail(sendResult.Errors);

            logger.Information(
                "Synchronized send from village {VillageId} to ({X}|{Y}) submitted, arrival {ArrivalTime}.",
                task.VillageId, task.TargetX, task.TargetY, sendResult.Value);

            return Result.Ok();
        }
    }
}
