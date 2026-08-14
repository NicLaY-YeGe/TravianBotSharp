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

            public Task(AccountId accountId, VillageId villageId, int targetX, int targetY)
                : base(accountId, villageId)
            {
                TargetX = targetX;
                TargetY = targetY;
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
