using MainCore.Commands.Features.DodgeTroop;
using MainCore.Commands.Features.SyncAttack;
using MainCore.Tasks.Base;

namespace MainCore.Tasks
{
    // One instance of this task exists per RaidListEntry row (NOT per village - a village can
    // have many rows, each on its own schedule), unlike every other VillageTask in this project
    // which is one-per-village (see Key override below). Each run: sends that row's troops as a
    // raid (RallyPointEventTypeEnums.AttackRaid) to its target, then reschedules ITSELF by
    // mutating task.ExecuteAt to now + a fresh random(IntervalMinMinutes, IntervalMaxMinutes) -
    // per TimerManager.Execute, a task whose ExecuteAt changed during its own run is re-ordered
    // and kept in the queue instead of being removed, which is what makes this self-repeating
    // without any external re-add. The row's NextExecuteAt is also persisted to the DB (not just
    // held on the in-memory task) so a bot/app restart doesn't lose the schedule - see
    // UpdateStorageCommand's bootstrap check, which re-adds a task at that exact time if one
    // isn't already queued.
    //
    // If the row was deleted or turned off (IsActive=false) since this task was queued, or the
    // Village/target no longer resolves, HandleAsync returns Skip.Error without touching
    // ExecuteAt - which per the same TimerManager rule means the task gets REMOVED instead of
    // rescheduled, i.e. disabling/deleting a row cleans up its task the next time it would have
    // fired (no separate cleanup pass needed).
    //
    // NOTE ON FAILURES: like every other command in this codebase, a Retry/Stop-class failure
    // from SendTroopsCommand (e.g. not enough troops in the target slot) pauses the WHOLE bot,
    // not just this one raid row - that's existing, consistent behavior (see TimerManager),
    // not something special-cased here. Worth knowing before relying on a large raid list
    // unattended: one row running out of troops stops everything until acknowledged.
    [Handler]
    public static partial class RaidListTask
    {
        public sealed class Task : VillageTask
        {
            public RaidListEntryId EntryId { get; }

            public Task(AccountId accountId, VillageId villageId, RaidListEntryId entryId) : base(accountId, villageId)
            {
                EntryId = entryId;
            }

            protected override string TaskName => "Raid list";

            // One task per ROW, not per village - the inherited "{AccountId}-{VillageId}" Key
            // would collide across every row from the same source village, so TaskManager would
            // treat them all as the same task (only the last AddOrUpdate would survive). Adding
            // the row id keeps every row independently queued and independently rescheduled.
            public override string Key => $"{AccountId}-{VillageId}-{EntryId}";
        }

        private static async ValueTask<Result> HandleAsync(
            Task task,
            AppDbContext context,
            ToSendTroopsPageCommand.Handler toSendTroopsPageCommand,
            SendTroopsCommand.Handler sendTroopsCommand,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            var entry = context.RaidListEntries.FirstOrDefault(x => x.Id == task.EntryId.Value);
            if (entry is null || !entry.IsActive)
            {
                return Skip.Error;
            }

            var toPageResult = await toSendTroopsPageCommand.HandleAsync(new(task.VillageId), cancellationToken);
            if (toPageResult.IsFailed) return toPageResult;

            var troopAmounts = entry.GetTroopAmounts();
            var sendResult = await sendTroopsCommand.HandleAsync(
                new(task.VillageId, entry.TargetX, entry.TargetY, RallyPointEventTypeEnums.AttackRaid, troopAmounts, Confirm: true, IncludeHero: entry.IncludeHero),
                cancellationToken);
            if (sendResult.IsFailed) return Result.Fail(sendResult.Errors);

            var minMinutes = Math.Max(1, entry.IntervalMinMinutes);
            var maxMinutes = Math.Max(minMinutes, entry.IntervalMaxMinutes);
            var delayMinutes = Random.Shared.Next(minMinutes, maxMinutes + 1);
            var nextExecuteAt = DateTime.Now.AddMinutes(delayMinutes);

            entry.NextExecuteAt = nextExecuteAt;
            context.SaveChanges();

            task.ExecuteAt = nextExecuteAt;

            logger.Information(
                "Raid list: sent from village {VillageId} to ({X}|{Y}), next send at {NextExecuteAt}.",
                task.VillageId, entry.TargetX, entry.TargetY, nextExecuteAt);

            return Result.Ok();
        }
    }
}
