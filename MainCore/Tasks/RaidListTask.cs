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
    // special-cased here. SendTroopsCommand itself still classifies a server rejection (e.g.
    // "There is no village at these coordinates." for an abandoned/conquered farm target) as a
    // Skip, not a Stop, since retrying the exact same bad coordinates will never succeed on its
    // own. BUT specifically for this "no village at these coordinates" case, RaidListTask
    // upgrades it to a Stop here (2026-08-25, user request): repeatedly hitting empty/abandoned
    // coordinates unattended looked like a real ban-risk pattern in the user's own logs (two
    // dead rows kept firing every few minutes all morning), so this row is deleted from the DB
    // outright and the whole bot is paused so the user notices and can review the rest of the
    // list, rather than silently dropping just this one task and moving on. Any OTHER rejection
    // reason (e.g. an alliance-protection message) still falls through to the generic
    // Result.Fail(...) below, unchanged.
    //
    // The one deliberate exception is a village simply not having enough troops for this row
    // right now (e.g. the previous wave hasn't returned yet) - that's routine for an unattended
    // raid list, not a ban-risk bug like the empty-target case above. Troop (and, if requested,
    // hero) availability is checked against the loaded Send Troops page BEFORE calling
    // SendTroopsCommand; if short, this run is skipped. BUT (2026-08-25, user request) rather
    // than silently rescheduling just this one row and moving on, the bot now pauses the ENTIRE
    // raid list (every active row for the account, same effect as RaidListViewModel's "Pause
    // all") and sends a Telegram notification (if NotifyOnPause is enabled) - see
    // PauseWholeListAndNotify below. Only the raid list feature is paused, not the whole bot.
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
            ITaskManager taskManager,
            ITelegramNotifier telegramNotifier,
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

            // Rolled ONCE per run (not per-slot as each is checked) so the amount we check
            // availability against is exactly the amount we send - see RollTroopAmounts.
            // Each row's Min/Max range (2026-08-22) means this genuinely varies run to run,
            // unlike the old fixed-amount behavior it falls back to for pre-2026-08-22 rows.
            var troopAmounts = entry.RollTroopAmounts(Random.Shared);

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
                    var reason = $"village {task.VillageId} doesn't have enough troops in slot {slot} (needs {amount}, has {available})";
                    await PauseWholeListAndNotify(task, context, taskManager, telegramNotifier, logger, reason, cancellationToken);
                    return Skip.Error;
                }
            }

            if (entry.IncludeHero)
            {
                const int heroSlot = 11;
                var heroAvailable = RallyPointSendTroopsParser.GetAvailableTroopCount(browser.Html, heroSlot);
                if (heroAvailable < 1)
                {
                    var reason = $"hero requested for village {task.VillageId} but not available";
                    await PauseWholeListAndNotify(task, context, taskManager, telegramNotifier, logger, reason, cancellationToken);
                    return Skip.Error;
                }
            }

            var sendResult = await sendTroopsCommand.HandleAsync(
                new(task.VillageId, entry.TargetX, entry.TargetY, RallyPointEventTypeEnums.AttackRaid, troopAmounts, Confirm: true, IncludeHero: entry.IncludeHero),
                cancellationToken);
            if (sendResult.IsFailed)
            {
                // "No village at these coordinates" means the target is permanently dead
                // (abandoned/conquered) - retrying it on schedule forever, unattended, is exactly
                // the kind of repeated-empty-coordinate pattern that risks flagging the account.
                // Delete the row so it can never fire again, and Stop the whole bot (not just
                // skip this row) so the user notices and reviews the rest of their raid list.
                var isEmptyTarget = sendResult.Errors.Any(e =>
                    e.Message.Contains("no village at these coordinates", StringComparison.OrdinalIgnoreCase));

                if (isEmptyTarget)
                {
                    context.RaidListEntries.Where(x => x.Id == task.EntryId.Value).ExecuteDelete();

                    logger.Warning(
                        "Raid list: ({X}|{Y}) from village {VillageId} has no village there (abandoned/conquered) - deleting this raid list row and stopping the bot so you can review the rest of the list.",
                        entry.TargetX, entry.TargetY, task.VillageId);

                    return Stop.Error.WithErrors(sendResult.Errors)
                        .WithError($"Raid list row targeting ({entry.TargetX}|{entry.TargetY}) was deleted - no village at that target.");
                }

                return Result.Fail(sendResult.Errors);
            }

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
        // Only called on the success path now - see PauseWholeListAndNotify below for what
        // happens on an insufficient-troops "skip" instead (2026-08-25 change).
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

        // 2026-08-25, user request: running out of troops for a raid isn't itself a ban risk (it
        // was previously just a silent per-row skip+reschedule - see the class-level comment,
        // now stale on this point), but the user wants to be alerted rather than have the bot
        // quietly keep trying other rows while a village sits empty-handed. So instead of
        // skipping just this one row, EVERY active row for this account is paused (same DB +
        // in-memory effect as RaidListViewModel's existing "Pause all" button - IsActive=false
        // and the queued RaidListTask.Task removed for each), and a Telegram message is sent (if
        // the account has NotifyOnPause enabled). This only pauses the raid list feature - unlike
        // the empty-target case above, the rest of the bot (building, adventures, etc.) is
        // untouched, since this is routine/expected (troops out on a wave) rather than a sign of
        // something wrong with the account.
        private static async System.Threading.Tasks.Task PauseWholeListAndNotify(
            Task task,
            AppDbContext context,
            ITaskManager taskManager,
            ITelegramNotifier telegramNotifier,
            ILogger logger,
            string reason,
            CancellationToken cancellationToken)
        {
            var entries = context.RaidListEntries
                .Where(x => x.AccountId == task.AccountId.Value && x.IsActive)
                .ToList();

            foreach (var entry in entries)
            {
                entry.IsActive = false;

                var queuedTask = taskManager.GetTaskList(task.AccountId)
                    .OfType<Task>()
                    .FirstOrDefault(t => t.EntryId.Value == entry.Id);
                if (queuedTask is not null) taskManager.Remove(task.AccountId, queuedTask);
            }

            context.SaveChanges();

            logger.Warning(
                "Raid list: {Reason} - pausing the whole raid list ({Count} active row(s)) instead of just skipping this one.",
                reason, entries.Count);

            var telegramSetting = telegramNotifier.Get(task.AccountId);
            if (telegramSetting.NotifyOnPause)
            {
                var username = context.Accounts.FirstOrDefault(x => x.Id == task.AccountId.Value)?.Username ?? $"{task.AccountId}";
                await telegramNotifier.NotifyAsync(task.AccountId, $"\u26D4 {username} - yagma listesi durduruldu: {reason}", cancellationToken);
            }
        }
    }
}
