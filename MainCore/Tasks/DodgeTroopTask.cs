using MainCore.Commands.Features.DodgeTroop;
using MainCore.Enums;
using MainCore.Models;
using MainCore.Tasks.Base;
using SendTroopsCommand = MainCore.Commands.Features.SyncAttack.SendTroopsCommand;

namespace MainCore.Tasks
{
    // Evolved 2026-08-13 from a single-slot REINFORCEMENT to a multi-slot ATTACK send (see
    // CLAUDE.md/PROJECT_CONTEXT.md §5m). Sending as an attack (rather than reinforcing one of
    // our own villages) means the target no longer has to be a real village - any coordinate
    // works, including an empty/nature tile - and lets us reuse SyncAttack's already
    // real-page-verified SendTroopsCommand instead of a bespoke reinforcement command.
    //
    // Verified official timing: a sent movement can be cancelled for 90 seconds after
    // sending (not the 1 minute originally assumed), and cancelling returns the troops as if
    // they'd travelled partway back, not instantly. Default schedule: send 30s before the
    // real attack lands, cancel 50s after sending - comfortably inside the 90s window while
    // still pulling the troops out well before impact.
    [Handler]
    public static partial class DodgeTroopTask
    {
        public sealed class Task : VillageTask
        {
            public Task(AccountId accountId, VillageId villageId) : base(accountId, villageId)
            {
            }

            protected override string TaskName => "Dodge troops";

            public override bool CanStart(AppDbContext context)
            {
                var enabled = context.BooleanByName(VillageId, VillageSettingEnums.DodgeEnable);
                if (!enabled) return false;

                var village = context.Villages.FirstOrDefault(x => x.Id == VillageId.Value);
                return village is not null && village.IsUnderAttack;
            }
        }

        // Bit (slot-1) of the DodgeTroopSlotsMask setting - slot is 1-10, tribe-relative order
        // (same convention as RallyPointTroopSlots/SyncAttack's troop selector).
        public static List<int> GetSelectedSlots(int mask)
        {
            var slots = new List<int>();
            for (var slot = 1; slot <= 10; slot++)
            {
                if ((mask & (1 << (slot - 1))) != 0) slots.Add(slot);
            }
            return slots;
        }

        private static async ValueTask<Result> HandleAsync(
            Task task,
            AppDbContext context,
            IChromeBrowser browser,
            ToRallyPointOverviewCommand.Handler toOverviewCommand,
            ToSendTroopsPageCommand.Handler toSendTroopsPageCommand,
            SendTroopsCommand.Handler sendTroopsCommand,
            ITaskManager taskManager,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            var overviewResult = await toOverviewCommand.HandleAsync(new(task.VillageId), cancellationToken);
            if (overviewResult.IsFailed)
            {
                if (overviewResult.HasError<MissingBuilding>())
                {
                    logger.Warning("No rally point in this village, cannot dodge.");
                    return Skip.Error.WithErrors(overviewResult.Errors);
                }
                return overviewResult;
            }

            var attackSeconds = RallyPointOverviewParser.GetIncomingAttackSeconds(browser.Html);
            if (attackSeconds is null)
            {
                // No incoming attack showing right now (already landed, already dodged, or
                // the IsUnderAttack flag was stale) - nothing to do this pass.
                return Skip.Error;
            }

            var sendBeforeSeconds = context.ByName(task.VillageId, VillageSettingEnums.DodgeSendSecondsBeforeImpact);
            if (sendBeforeSeconds <= 0) sendBeforeSeconds = 30;

            var secondsUntilSend = attackSeconds.Value - sendBeforeSeconds;

            // Not yet time to send - reschedule THIS SAME task instance to fire right at the
            // send moment rather than sending now. Mutating ExecuteAt in place (instead of
            // queueing a separate task) matches the self-reschedule pattern already used by
            // TrainTroopTask/NextExecuteTrainTroopTaskCommand: the task stays in the queue and
            // this handler simply runs again once ExecuteAt is reached.
            if (secondsUntilSend > 5)
            {
                task.ExecuteAt = DateTime.Now.AddSeconds(secondsUntilSend);
                logger.Information("Incoming attack on {VillageId} lands in {Seconds}s - will send dodge troops in {SendIn}s.",
                    task.VillageId, attackSeconds.Value, secondsUntilSend);
                return Result.Ok();
            }

            var slotsMask = context.ByName(task.VillageId, VillageSettingEnums.DodgeTroopSlotsMask);
            var selectedSlots = GetSelectedSlots(slotsMask);
            if (selectedSlots.Count == 0)
            {
                logger.Warning("Dodge is enabled on {VillageId} but no troop types are selected.", task.VillageId);
                return Skip.Error;
            }

            var targetX = context.ByName(task.VillageId, VillageSettingEnums.DodgeTargetX);
            var targetY = context.ByName(task.VillageId, VillageSettingEnums.DodgeTargetY);

            var sendPageResult = await toSendTroopsPageCommand.HandleAsync(new(task.VillageId), cancellationToken);
            if (sendPageResult.IsFailed) return sendPageResult;

            var troopAmounts = new Dictionary<int, long>();
            foreach (var slot in selectedSlots)
            {
                var available = RallyPointSendTroopsParser.GetAvailableTroopCount(browser.Html, slot);
                if (available > 0) troopAmounts[slot] = available;
            }

            if (troopAmounts.Count == 0)
            {
                logger.Information("No troops available in the selected slots to dodge with in {VillageId}.", task.VillageId);
                return Skip.Error;
            }

            var sendResult = await sendTroopsCommand.HandleAsync(
                new(task.VillageId, targetX, targetY, RallyPointEventTypeEnums.AttackNormal, troopAmounts, Confirm: true),
                cancellationToken);
            if (sendResult.IsFailed) return Result.Fail(sendResult.Errors);

            var recallAfterSeconds = context.ByName(task.VillageId, VillageSettingEnums.DodgeRecallSecondsAfterSend);
            if (recallAfterSeconds <= 0) recallAfterSeconds = 50;

            var recallTask = new RecallTroopTask.Task(task.AccountId, task.VillageId, targetX, targetY)
            {
                ExecuteAt = DateTime.Now.AddSeconds(recallAfterSeconds),
            };
            taskManager.Add(recallTask);

            logger.Information("Dodged {Count} troop type(s) from village {VillageId} to ({X}|{Y}); will recall in {Seconds}s.",
                troopAmounts.Count, task.VillageId, targetX, targetY, recallAfterSeconds);

            return Result.Ok();
        }
    }
}
