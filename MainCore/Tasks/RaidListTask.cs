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
    // from SendTroopsCommand (e.g. a parser/UI problem) pauses the WHOLE bot, not just this one
    // raid row - that's existing, consistent behavior (see TimerManager), not something
    // special-cased here. The one exception SendTroopsCommand itself makes is a target that the
    // server flat-out rejects (e.g. "There is no village at these coordinates." for an
    // abandoned/conquered farm target) - that comes back as Skip.Error, not Stop.Error, since
    // retrying the exact same bad coordinates will never succeed. Note this Skip path does NOT
    // call RescheduleNext (unlike the insufficient-troops Skip below), so per TimerManager's
    // "ExecuteAt unchanged -> remove" rule this row's task is dropped from the queue rather than
    // retried - it comes back only on the next app restart's UpdateStorageCommand bootstrap
    // check, giving it one more attempt before being dropped again. Fixing the row's target (or
    // disabling it) is on the user; the bot won't spin on it in the meantime.
    //
    // The one deliberate exception is a village simply not having enough troops for this row
    // right now (e.g. the previous wave hasn't returned yet) - that's routine for an unattended
    // raid list, not a bug worth pausing the whole bot over. So troop (and, if requested, hero)
    // availability is checked against the loaded Send Troops page BEFORE calling
    // SendTroopsCommand; if short, this run is skipped and the row is rescheduled exactly like a
    // normal send (see RescheduleNext), silently, with only an Information-level log line - the
    // bot moves straight on to whatever's next in the queue.
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
            IChromeBrowser browser,
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

            // Pre-check availability ourselves rather than letting SendTroopsCommand's own check
            // fail the send - its failure there is a generic Retry (shared with every other
            // caller, e.g. sync attack, where running short really should pause the bot), so it
            // can't be told apart from a real problem. Checking here first lets us treat "not
            // enough troops yet" as routine instead.
            foreach (var (slot, amount) in troopAmounts)
            {
                if (amount <= 0) continue;

                var available = RallyPointSendTroopsParser.GetAvailableTroopCount(browser.Html, slot);
                if (available < amount)
                {
                    var skipNextAt = RescheduleNext(task, entry, context);
                    logger.Information(
                        "Raid list: village {VillageId} doesn't have enough troops in slot {Slot} yet (need {Needed}, have {Available}) - skipping this run, next attempt at {NextExecuteAt}.",
                        task.VillageId, slot, amount, available, skipNextAt);
                    return Skip.Error;
                }
            }

            if (entry.IncludeHero)
            {
                const int heroSlot = 11;
                var heroAvailable = RallyPointSendTroopsParser.GetAvailableTroopCount(browser.Html, heroSlot);
                if (heroAvailable < 1)
                {
                    var skipNextAt = RescheduleNext(task, entry, context);
                    logger.Information(
                        "Raid list: hero requested for village {VillageId} but not available - skipping this run, next attempt at {NextExecuteAt}.",
                        task.VillageId, skipNextAt);
                    return Skip.Error;
                }
            }

            var sendResult = await sendTroopsCommand.HandleAsync(
                new(task.VillageId, entry.TargetX, entry.TargetY, RallyPointEventTypeEnums.AttackRaid, troopAmounts, Confirm: true, IncludeHero: entry.IncludeHero),
                cancellationToken);
            if (sendResult.IsFailed) return Result.Fail(sendResult.Errors);

            var nextExecuteAt = RescheduleNext(task, entry, context);

            logger.Information(
                "Raid list: sent from village {VillageId} to ({X}|{Y}), next send at {NextExecuteAt}.",
                task.VillageId, entry.TargetX, entry.TargetY, nextExecuteAt);

            return Result.Ok();
        }

        // Picks the row's next fire time (its own independent random(min,max) window) and
        // persists it both to the DB row (NextExecuteAt, so a restart doesn't lose the schedule)
        // and the in-memory task (ExecuteAt, so TimerManager's "ExecuteAt changed -> reschedule
        // instead of remove" rule keeps this task self-repeating - see the class-level comment).
        // Shared by the success path and every "skip this run" path so a skipped row is
        // rescheduled exactly like a sent one.
        private static DateTime RescheduleNext(Task task, RaidListEntry entry, AppDbContext context)
        {
            var minMinutes = Math.Max(1, entry.IntervalMinMinutes);
            var maxMinutes = Math.Max(minMinutes, entry.IntervalMaxMinutes);
            var delayMinutes = Random.Shared.Next(minMinutes, maxMinutes + 1);
            var nextExecuteAt = DateTime.Now.AddMinutes(delayMinutes);

            entry.NextExecuteAt = nextExecuteAt;
            context.SaveChanges();

            task.ExecuteAt = nextExecuteAt;

            return nextExecuteAt;
        }
    }
}
