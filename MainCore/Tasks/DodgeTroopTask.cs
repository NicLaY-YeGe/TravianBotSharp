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
    //
    // 2026-09-18 real-log incident + fix: a live account's attack landed at 23:47:48, but the
    // task's own "should I send now?" pass didn't start until 23:47:43 - only 5s of real
    // runway - and by the time it had re-navigated to Rally Point Overview, reloaded the
    // whole Rally Point building AGAIN just to reach the Send Troops tab (via
    // ToSendTroopsPageCommand duplicating ToRallyPointOverviewCommand's ToDorf/
    // UpdateBuilding/ToBuildingByType steps from scratch instead of just switching tabs on
    // the page it was already sitting on), and parsed troop counts, the attack had already
    // landed and wiped the village's troops (~9s elapsed just to reach that point, per the
    // log). Two independent problems, both fixed here: (1) the old flat "proceed if <=5s
    // until the configured send-before point" threshold had no relationship to how long the
    // actual send sequence takes to run - replaced with MinimumSendLeadTimeSeconds, sized
    // from that log's measured timings with a margin, matching the existing
    // WaveAttackPlanner.SafetyBuffer (15s) precedent elsewhere in this codebase for the same
    // kind of automation-delay allowance. (2) the send-page navigation below now switches
    // directly to the Send Troops tab (SwitchTabCommand) instead of going through
    // ToSendTroopsPageCommand, since this task is always already sitting on the Rally Point
    // building (just loaded by ToRallyPointOverviewCommand above) - reloading Dorf+the whole
    // building again was pure wasted time on the only path where every second counts.
    // ToSendTroopsPageCommand itself is left untouched since OasisScoutTask/SyncAttackPlanTask/
    // RaidListTask/WaveAttackPlanTask/SendTroopsAtTimeTask all call it from elsewhere and
    // don't have this same "already on the page" precondition.
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

        // Rally Point tab order verified from a real page capture (same convention as
        // ToRallyPointOverviewCommand/ToSendTroopsPageCommand): 0 = Management, 1 = Overview,
        // 2 = Send troops, 3 = Simulators, 4 = Farm List. Kept as a local copy rather than
        // reusing ToSendTroopsPageCommand's own constant since this task intentionally
        // bypasses that command's full re-navigation (see the streamlined tab-switch below).
        private const int SendTroopsTabIndex = 2;

        // Conservative real-world floor for how long it takes, from the moment we decide to
        // send, to actually finish sending: a Send Troops tab switch (~1-2s) + parsing
        // available troop counts (instant) + SendTroopsCommand's own fill/submit/confirm
        // round trip. Sized with a safety margin above the 2026-09-18 live-log measurement
        // (see the class comment above) and deliberately kept in line with
        // WaveAttackPlanner.SafetyBuffer (15s) elsewhere in this codebase, rather than
        // invented fresh, since both exist for the same reason: buffering real automation
        // delay against a hard external deadline.
        private const int MinimumSendLeadTimeSeconds = 15;

        private static async ValueTask<Result> HandleAsync(
            Task task,
            AppDbContext context,
            IChromeBrowser browser,
            ToRallyPointOverviewCommand.Handler toOverviewCommand,
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
            //
            // Threshold is MinimumSendLeadTimeSeconds (not the old flat 5s) - see the class
            // comment for why: 5s was never enough for the send sequence itself to complete.
            if (secondsUntilSend > MinimumSendLeadTimeSeconds)
            {
                task.ExecuteAt = DateTime.Now.AddSeconds(secondsUntilSend);
                logger.Information("Incoming attack on {VillageId} lands in {Seconds}s - will send dodge troops in {SendIn}s.",
                    task.VillageId, attackSeconds.Value, secondsUntilSend);
                return Result.Ok();
            }

            // Even though it's "time" per the configured DodgeSendSecondsBeforeImpact lead,
            // check the REAL remaining time against actual impact (attackSeconds, not the
            // offset secondsUntilSend) before committing to a send attempt. If there isn't
            // even MinimumSendLeadTimeSeconds of real runway left, the send sequence cannot
            // finish before the attack lands regardless of what we do - this is exactly the
            // 2026-09-18 scenario (task woke with only ~5s left). Skip cleanly with a clear
            // reason instead of repeating that: burning several more seconds mid-navigation
            // only to fail on "no troops available" after they've already been destroyed.
            if (attackSeconds.Value < MinimumSendLeadTimeSeconds)
            {
                logger.Warning(
                    "Incoming attack on {VillageId} lands in {Seconds}s - too little real time left to complete a dodge send (~{MinLeadTime}s needed), skipping this attempt.",
                    task.VillageId, attackSeconds.Value, MinimumSendLeadTimeSeconds);
                return Skip.Error;
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

            // Streamlined vs. ToSendTroopsPageCommand (see class comment): we're already
            // sitting on the Rally Point building from ToRallyPointOverviewCommand above, so
            // just switch tabs instead of reloading Dorf + the whole building from scratch.
            var switchTabResult = await SwitchTabCommand.SwitchTab(browser, SendTroopsTabIndex, cancellationToken);
            if (switchTabResult.IsFailed) return switchTabResult;

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
