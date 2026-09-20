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
    //
    // 2026-09-20 UPDATE 2: the "no village at these coordinates" case above no longer deletes the
    // row nor stops the bot - it marks the row IsDeadTarget (inactive) and carries on; see the
    // dead-target check in HandleAsync.
    //
    // 2026-09-20 UPDATE: the gate's spacing is no longer the sent row's own interval but a
    // separate account setting in SECONDS (RaidListSendGapMin/MaxSeconds, default 30-90) - the
    // rest of the paragraph below is the original design. The gate value moved to
    // RaidListNextAllowedSendAtSeconds (the old Minutes key is unused now).
    //
    // ACCOUNT-WIDE SEND GATE (2026-09-16, user request): each row still keeps its own
    // IntervalMinMinutes/IntervalMaxMinutes and reschedules ITSELF via RescheduleNext exactly
    // as before, but a send is now also gated behind a single account-wide "earliest allowed
    // next send" timestamp (AccountSettingEnums.RaidListNextAllowedSendAtMinutes - see its own
    // comment for why). If this row's turn comes up before that gate, it doesn't hit the Rally
    // Point at all - it just re-parks itself at the gate time (same Skip.Error + mutate
    // ExecuteAt deferral pattern already used elsewhere, e.g. AccountTaskBehavior's 30-minute
    // retry) and tries again later. Every successful send - from ANY row - pushes the gate
    // forward by a fresh random(that row's own Min, Max). Net effect: a big bulk-added list no
    // longer fires several rows within seconds of each other by chance; the whole list behaves
    // like one continuous, randomly-spaced chain, same as a human clicking raids by hand.
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

            // Dead-target memory (2026-09-20, user request): this exact target already answered
            // "no village at these coordinates" for another row of this account (or for this one,
            // e.g. the user switched it back on). Ignore it quietly - no browser, no send, and
            // NOT a bot stop - and switch the row off so it stops re-queueing; the task is
            // removed by the Skip.Error (ExecuteAt untouched).
            if (entry.IsDeadTarget || context.RaidListEntries.Any(x =>
                    x.AccountId == entry.AccountId
                    && x.TargetX == entry.TargetX
                    && x.TargetY == entry.TargetY
                    && x.IsDeadTarget))
            {
                entry.IsDeadTarget = true;
                entry.IsActive = false;
                context.SaveChanges();

                logger.Information(
                    "Raid list: ({X}|{Y}) is a known dead target (no village there) - ignoring this row without contacting the server.",
                    entry.TargetX, entry.TargetY);

                return Skip.Error;
            }

            // Account-wide gate check - see class-level comment. Deliberately done BEFORE
            // navigating to the Send Troops page at all: if this row is going to be deferred
            // anyway, there's no reason to load a page and burn browser activity for it.
            var gateSeconds = context.ByName(task.AccountId, AccountSettingEnums.RaidListNextAllowedSendAtSeconds);
            if (gateSeconds > 0)
            {
                var gateTime = FromGateSeconds(gateSeconds);
                if (DateTime.Now < gateTime)
                {
                    entry.NextExecuteAt = gateTime;
                    context.SaveChanges();

                    task.ExecuteAt = gateTime;

                    logger.Information(
                        "Raid list: village {VillageId} -> ({X}|{Y}) is due, but another row already claimed the next send slot - deferring to {GateTime}.",
                        task.VillageId, entry.TargetX, entry.TargetY, gateTime);

                    return Skip.Error;
                }
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
                // Mark the row as a dead target (inactive, remembered) so it can never fire
                // again. (Until 2026-09-20 this also Stopped the whole bot; not any more.)
                var isEmptyTarget = sendResult.Errors.Any(e =>
                    e.Message.Contains("no village at these coordinates", StringComparison.OrdinalIgnoreCase));

                if (isEmptyTarget)
                {
                    // 2026-09-20: keep the row as an inactive "dead target" marker instead of
                    // deleting it, so the same coordinate entered again later is ignored (see the
                    // dead-target check at the top of this method and RaidListEntry.IsDeadTarget).
                    entry.IsDeadTarget = true;
                    entry.IsActive = false;
                    context.SaveChanges();

                    logger.Warning(
                        "Raid list: ({X}|{Y}) from village {VillageId} has no village there (abandoned/conquered) - marked as a dead target (row kept inactive, coordinate ignored from now on); the bot keeps running.",
                        entry.TargetX, entry.TargetY, task.VillageId);

                    // 2026-09-20 (later, user request): no longer a bot Stop - the row is already
                    // switched off above, so a plain Skip.Error (ExecuteAt untouched -> this task
                    // is removed from the queue) is enough and every other row carries on.
                    return Skip.Error;
                }

                return Result.Fail(sendResult.Errors);
            }

            var nextExecuteAt = RescheduleNext(task, entry, context);
            var gateAdvancedTo = AdvanceGlobalSendGate(context, task.AccountId);

            logger.Information(
                "Raid list: sent from village {VillageId} to ({X}|{Y}), this row's next send at {NextExecuteAt}, next send for ANY row not before {GateTime}.",
                task.VillageId, entry.TargetX, entry.TargetY, nextExecuteAt, gateAdvancedTo);

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

        // Pushes the account-wide send gate forward by random(SendGapMin, SendGapMax) SECONDS
        // (account settings RaidListSendGapMinSeconds/MaxSeconds) from now - called once, right
        // after a successful send. 2026-09-20, user request: this used to re-use the sent row's
        // own repeat interval (minutes), which stretched a big list into a round of many hours
        // and left troops idle. Now the chain spacing is its own short range, while each row
        // still repeats on its own IntervalMin/MaxMinutes (RescheduleNext). Stored as seconds
        // since GateEpoch in a plain int AccountSetting (see the enum comment).
        private static DateTime AdvanceGlobalSendGate(AppDbContext context, AccountId accountId)
        {
            var minSeconds = Math.Max(1, context.ByName(accountId, AccountSettingEnums.RaidListSendGapMinSeconds));
            var maxSeconds = Math.Max(minSeconds, context.ByName(accountId, AccountSettingEnums.RaidListSendGapMaxSeconds));
            var gapSeconds = Random.Shared.Next(minSeconds, maxSeconds + 1);
            var gateTime = DateTime.Now.AddSeconds(gapSeconds);

            var setting = context.AccountsSetting.FirstOrDefault(x =>
                x.AccountId == accountId.Value && x.Setting == AccountSettingEnums.RaidListNextAllowedSendAtSeconds);

            if (setting is null)
            {
                context.AccountsSetting.Add(new AccountSetting
                {
                    AccountId = accountId.Value,
                    Setting = AccountSettingEnums.RaidListNextAllowedSendAtSeconds,
                    Value = ToGateSeconds(gateTime),
                });
            }
            else
            {
                setting.Value = ToGateSeconds(gateTime);
            }

            context.SaveChanges();

            return gateTime;
        }

        private static readonly DateTimeOffset GateEpoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        private static int ToGateSeconds(DateTime dt) => (int)(new DateTimeOffset(dt).ToUnixTimeSeconds() - GateEpoch.ToUnixTimeSeconds());

        private static DateTime FromGateSeconds(int seconds) => DateTimeOffset.FromUnixTimeSeconds(GateEpoch.ToUnixTimeSeconds() + seconds).LocalDateTime;

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
