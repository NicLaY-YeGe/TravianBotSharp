using MainCore.Commands.Features.DefendDonate;
using MainCore.Commands.Features.DodgeTroop;
using MainCore.Enums;
using MainCore.Parsers;
using MainCore.Tasks.Base;

namespace MainCore.Tasks
{
    // Added 2026-09-27 per explicit user request: when an attack is incoming, spend the
    // resources sitting ABOVE the village's Cranny (Hideout) protection first on training the
    // tribe's cheapest troop (T1 infantry, always Barracks), then donate whatever excess is
    // still left over to the alliance's Recruitment bonus - so nothing above what the Cranny
    // already protects gets handed to the attacker as loot. The whole sequence (train +
    // donate) must be FINISHED at least FinishBeforeImpactSeconds before the attack lands
    // (explicit user requirement), not merely started by then.
    //
    // Scheduling follows the same self-reschedule pattern as DodgeTroopTask: this task wakes
    // up repeatedly, checks the real remaining time via RallyPointOverviewParser (same source
    // Dodge uses), and only actually runs the train+donate sequence once there's just enough
    // real time left to both finish it AND still land before the FinishBeforeImpactSeconds
    // deadline. MinimumActionTimeSeconds (the estimated real duration of the whole sequence -
    // Barracks nav+fill+submit, then Alliance page nav+select+fill x4+submit) is a rough
    // estimate, not yet measured against a live run - re-tune if logs show it running long.
    [Handler]
    public static partial class DefendDonateTask
    {
        public sealed class Task : VillageTask
        {
            public Task(AccountId accountId, VillageId villageId) : base(accountId, villageId)
            {
            }

            // 2026-10-03: wake window (see AttackWakeWindow), same idea as DodgeTroopTask.
            public DateTime? WakeFrom { get; private set; }
            public DateTime? WakeUntil { get; private set; }

            public void SetWakeWindow(DateTime from, DateTime until)
            {
                WakeFrom = from;
                WakeUntil = until;
            }

            public override bool BypassOnlineHours => WakeFrom is not null;
            public override DateTime? WakeStart => WakeFrom;
            public override DateTime? WakeEnd => WakeUntil;

            protected override string TaskName => "Defend: train + donate excess resources";

            public override bool CanStart(AppDbContext context)
            {
                var enabled = context.BooleanByName(VillageId, VillageSettingEnums.DefendDonateEnable);
                if (!enabled) return false;

                var village = context.Villages.FirstOrDefault(x => x.Id == VillageId.Value);
                return village is not null && village.IsUnderAttack;
            }
        }

        // User's explicit requirement: everything must be DONE at least this long before the
        // attack lands.
        private const int FinishBeforeImpactSeconds = 60;

        // Rough estimate of how long the train+donate sequence itself takes once started:
        // Barracks nav (~1-2s) + input fill + submit, then Alliance page nav + radio click +
        // 4 resource inputs + submit. Kept deliberately generous given the extra page
        // navigation (Alliance) this task does beyond what DodgeTroopTask needs.
        private const int MinimumActionTimeSeconds = 45;

        private static async ValueTask<Result> HandleAsync(
            Task task,
            AppDbContext context,
            IChromeBrowser browser,
            ToRallyPointOverviewCommand.Handler toOverviewCommand,
            GetCrannyProtectionCommand.Handler getCrannyProtectionCommand,
            TrainCheapestTroopCommand.Handler trainCheapestTroopCommand,
            DonateAllianceBonusCommand.Handler donateAllianceBonusCommand,
            ITaskManager taskManager,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            var overviewResult = await toOverviewCommand.HandleAsync(new(task.VillageId), cancellationToken);
            if (overviewResult.IsFailed)
            {
                if (overviewResult.HasError<MissingBuilding>())
                {
                    logger.Warning("No rally point in {VillageId}, cannot time the defend+donate action.", task.VillageId);
                    return Skip.Error.WithErrors(overviewResult.Errors);
                }
                return overviewResult;
            }

            var attackSeconds = RallyPointOverviewParser.GetIncomingAttackSeconds(browser.Html);
            if (attackSeconds is null)
            {
                // No incoming attack showing right now (already landed, resolved, or the
                // IsUnderAttack flag was stale) - nothing to do this pass.
                return Skip.Error;
            }

            var secondsUntilStart = attackSeconds.Value - FinishBeforeImpactSeconds - MinimumActionTimeSeconds;

            if (secondsUntilStart > 0)
            {
                task.ExecuteAt = DateTime.Now.AddSeconds(secondsUntilStart);
                AttackWakeWindow.Apply(context, taskManager, task.AccountId, task.ExecuteAt, DateTime.Now.AddSeconds(attackSeconds.Value), task.SetWakeWindow, logger);
                logger.Information(
                    "Incoming attack on {VillageId} lands in {Seconds}s - will train+donate in {StartIn}s so it finishes {Deadline}s before impact.",
                    task.VillageId, attackSeconds.Value, secondsUntilStart, FinishBeforeImpactSeconds);
                return Result.Ok();
            }

            // Not enough real runway left to finish by the FinishBeforeImpactSeconds deadline.
            // If there's still enough time to finish before actual impact, proceed anyway
            // (partial protection late is better than none) but say so clearly; only give up
            // entirely if even that's no longer possible.
            if (attackSeconds.Value < MinimumActionTimeSeconds)
            {
                logger.Warning(
                    "Incoming attack on {VillageId} lands in {Seconds}s - too little real time left to complete the defend+donate sequence (~{MinTime}s needed), skipping.",
                    task.VillageId, attackSeconds.Value, MinimumActionTimeSeconds);
                return Skip.Error;
            }

            if (attackSeconds.Value - MinimumActionTimeSeconds < FinishBeforeImpactSeconds)
            {
                logger.Warning(
                    "Incoming attack on {VillageId} lands in {Seconds}s - the {Deadline}s-before-impact goal will be missed, proceeding anyway since there's still time before actual impact.",
                    task.VillageId, attackSeconds.Value, FinishBeforeImpactSeconds);
            }

            var storage = context.Storages.FirstOrDefault(x => x.VillageId == task.VillageId.Value);
            if (storage is null)
            {
                logger.Warning("No known storage levels for {VillageId} yet, skipping defend+donate.", task.VillageId);
                return Skip.Error;
            }

            var protectionResult = await getCrannyProtectionCommand.HandleAsync(new(task.VillageId), cancellationToken);
            if (protectionResult.IsFailed) return Result.Fail(protectionResult.Errors);
            var protectedAmount = protectionResult.Value;

            var excessWood = Math.Max(0, storage.Wood - protectedAmount);
            var excessClay = Math.Max(0, storage.Clay - protectedAmount);
            var excessIron = Math.Max(0, storage.Iron - protectedAmount);
            var excessCrop = Math.Max(0, storage.Crop - protectedAmount);

            if (excessWood == 0 && excessClay == 0 && excessIron == 0 && excessCrop == 0)
            {
                logger.Information("No resources above Cranny protection in {VillageId}, nothing to train or donate.", task.VillageId);
                return Result.Ok();
            }

            var trainResult = await trainCheapestTroopCommand.HandleAsync(
                new(task.VillageId, excessWood, excessClay, excessIron, excessCrop), cancellationToken);
            if (trainResult.IsFailed) return Result.Fail(trainResult.Errors);

            // Re-read the CURRENT stock straight off whatever page training left us on (the
            // resource stock bar is present on every in-game page, per the top-bar markup
            // seen on every captured page so far) rather than tracking consumed-per-resource
            // ourselves - simpler and immune to any per-unit-cost parsing drift.
            var remainingWood = Math.Max(0, StorageParser.GetWood(browser.Html) - protectedAmount);
            var remainingClay = Math.Max(0, StorageParser.GetClay(browser.Html) - protectedAmount);
            var remainingIron = Math.Max(0, StorageParser.GetIron(browser.Html) - protectedAmount);
            var remainingCrop = Math.Max(0, StorageParser.GetCrop(browser.Html) - protectedAmount);

            if (remainingWood == 0 && remainingClay == 0 && remainingIron == 0 && remainingCrop == 0)
            {
                logger.Information("Trained {Count} troop(s) in {VillageId}; nothing left over to donate.", trainResult.Value, task.VillageId);
                return Result.Ok();
            }

            var donateResult = await donateAllianceBonusCommand.HandleAsync(
                new(task.VillageId, remainingWood, remainingClay, remainingIron, remainingCrop), cancellationToken);
            if (donateResult.IsFailed) return Result.Fail(donateResult.Errors);

            logger.Information(
                "Defend+donate done for {VillageId}: trained {TroopCount} troop(s), donated {Donated} resources to the alliance.",
                task.VillageId, trainResult.Value, donateResult.Value);

            return Result.Ok();
        }
    }
}
