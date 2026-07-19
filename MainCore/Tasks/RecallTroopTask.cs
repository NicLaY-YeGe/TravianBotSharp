using MainCore.Commands.Features.DodgeTroop;
using MainCore.Tasks.Base;

namespace MainCore.Tasks
{
    [Handler]
    public static partial class RecallTroopTask
    {
        // Runs on the village that dodged, some minutes after its troops arrived at the
        // nearest village. Scheduling (task.ExecuteAt) is set by DodgeTroopTask when it
        // sends the reinforcement out - this task just does the recall click when it's due.
        public sealed class Task : VillageTask
        {
            public Task(AccountId accountId, VillageId villageId) : base(accountId, villageId)
            {
            }

            protected override string TaskName => "Recall dodge troops";
        }

        private static async ValueTask<Result> HandleAsync(
            Task task,
            AppDbContext context,
            ToRallyPointOverviewCommand.Handler toOverviewCommand,
            RecallTroopCommand.Handler recallTroopCommand,
            CancellationToken cancellationToken)
        {
            var targetId = DodgeTroopTask.GetNearestVillage(context, task.AccountId, task.VillageId);
            if (targetId is null) return Skip.Error;

            var targetVillage = context.Villages.FirstOrDefault(x => x.Id == targetId.Value.Value);
            if (targetVillage is null) return Skip.Error;

            var result = await toOverviewCommand.HandleAsync(new(task.VillageId), cancellationToken);
            if (result.IsFailed) return result;

            result = await recallTroopCommand.HandleAsync(new(task.VillageId, targetVillage.Name), cancellationToken);
            if (result.IsFailed) return result;

            return Result.Ok();
        }
    }
}
