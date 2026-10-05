using MainCore.Commands.Features.DodgeTroop;
using MainCore.Models;
using MainCore.Tasks.Base;

namespace MainCore.Tasks
{
    // Evolved 2026-08-13 alongside DodgeTroopTask: cancels the ATTACK-type dodge movement
    // sent to a fixed coordinate, instead of the old reinforcement sent to one of our own
    // villages. Targets a raw (X|Y) coordinate now, not a village name/id, since attack
    // movements don't need a real destination village.
    [Handler]
    public static partial class RecallTroopTask
    {
        public sealed class Task : VillageTask
        {
            public int TargetX { get; }
            public int TargetY { get; }

            // 2026-10-03: inherits the dodge's wake window (see AttackWakeWindow) so the recall,
            // due ~50s after the dodge send, isn't held back by an offline hour.
            public DateTime? WakeFrom { get; }
            public DateTime? WakeUntil { get; }

            public override bool BypassOnlineHours => WakeFrom is not null;
            public override DateTime? WakeStart => WakeFrom;
            public override DateTime? WakeEnd => WakeUntil;

            public Task(AccountId accountId, VillageId villageId, int targetX, int targetY, DateTime? wakeFrom = null, DateTime? wakeUntil = null)
                : base(accountId, villageId)
            {
                TargetX = targetX;
                TargetY = targetY;
                WakeFrom = wakeFrom;
                WakeUntil = wakeUntil;
            }

            protected override string TaskName => $"Recall dodge troops from ({TargetX}|{TargetY})";
        }

        private static async ValueTask<Result> HandleAsync(
            Task task,
            ToRallyPointOverviewCommand.Handler toOverviewCommand,
            RecallTroopCommand.Handler recallTroopCommand,
            CancellationToken cancellationToken)
        {
            var result = await toOverviewCommand.HandleAsync(new(task.VillageId), cancellationToken);
            if (result.IsFailed) return result;

            result = await recallTroopCommand.HandleAsync(new(task.VillageId, task.TargetX, task.TargetY), cancellationToken);
            if (result.IsFailed) return result;

            return Result.Ok();
        }
    }
}
