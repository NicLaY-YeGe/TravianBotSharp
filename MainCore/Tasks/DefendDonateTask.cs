using MainCore.Commands.Features.DefendDonate;
using MainCore.Commands.Features.DodgeTroop;
using MainCore.Commands.Features.SendResource;
using MainCore.Enums;
using MainCore.Parsers;
using MainCore.Tasks.Base;

namespace MainCore.Tasks
{
    // Reworked 2026-10-19 per explicit user decision (the 09-27 "train + donate to the alliance"
    // version hung 180 s on the Alliance Bonus page and pushed Dodge past impact - CHANGELOG.md).
    // The "Donate" in the name is historical; the village setting / task class keep it so saved
    // settings and the dispatch in CustomServiceScopeFactory stay valid.
    //
    // New behaviour, ONCE per incoming attack, right when the attack is seen:
    //   1. train the tribe's cheapest troop (T1 infantry, Barracks) with the village's whole stock;
    //   2. send EVERYTHING that is left (Cranny protection is deliberately ignored) with the
    //      Marketplace to another own village - random, but a village whose storage has room is
    //      preferred, villages under attack are avoided (DefendResourceTargetRules);
    //   3. no Marketplace / no free merchant -> nothing is sent, the task just ends;
    //   4. never run again for this attack, whatever the outcome (DefendAttackMemory).
    // Top priority is that Dodge still works: the whole sequence runs under a hard time budget
    // that ends before Dodge's send moment, and every failure is Skip (never Retry/Stop, which
    // would hold the single-threaded queue or pause the account - CLAUDE.md 2l).
    [Handler]
    public static partial class DefendDonateTask
    {
        public sealed class Task : VillageTask
        {
            public Task(AccountId accountId, VillageId villageId) : base(accountId, villageId)
            {
            }

            protected override string TaskName => "Defend: train + send resources away";

            public override bool CanStart(AppDbContext context)
            {
                var enabled = context.BooleanByName(VillageId, VillageSettingEnums.DefendDonateEnable);
                if (!enabled) return false;

                // Already handled for the attack that is on its way: do not queue it again.
                if (DefendAttackMemory.IsHandled(VillageId.Value, DateTime.Now)) return false;

                var village = context.Villages.FirstOrDefault(x => x.Id == VillageId.Value);
                return village is not null && village.IsUnderAttack;
            }
        }

        // The attack counts as handled until this long after its impact.
        private const int HandledGraceSeconds = 90;

        // Seconds kept free before impact when Dodge is off.
        private const int ReserveBeforeImpactSeconds = 15;

        // Extra gap kept before Dodge's send moment (Dodge send lead + this) when Dodge is on.
        private const int DodgeMarginSeconds = 15;

        // Below this budget the sequence is not even started: it cannot finish, and the time
        // belongs to Dodge. Rough estimate (Barracks nav+train ~10-15 s, Marketplace nav+send
        // ~15-20 s), not yet measured live - re-tune from logs.
        private const int MinimumActionTimeSeconds = 25;

        // 2026-10-19: a secondary job racing an attack. A Retry/Stop result would make the timer
        // re-run it and, after 3 retries, pause the whole account - and every Retry holds the
        // single-threaded queue (CLAUDE.md 2l). Any failure here is logged and skipped.
        private static Result DefendFailed(IEnumerable<IError> errors, ILogger logger)
        {
            logger.Warning("Defend step failed, skipping so Dodge is not blocked: {Errors}",
                string.Join(" | ", errors.Select(e => e.Message)));
            return Skip.Error.WithErrors(errors);
        }

        // A user pause (the task's own token) must still surface as Cancel; everything else,
        // including the budget running out, is a plain Skip.
        private static Result Abort(CancellationToken pauseToken, IEnumerable<IError> errors, ILogger logger)
        {
            if (pauseToken.IsCancellationRequested) return Cancel.Error;
            return DefendFailed(errors, logger);
        }

        // Seconds the sequence may use: it has to be over before Dodge's send moment.
        private static int GetBudgetSeconds(AppDbContext context, VillageId villageId, int attackSeconds)
        {
            var reserve = ReserveBeforeImpactSeconds;
            if (context.BooleanByName(villageId, VillageSettingEnums.DodgeEnable))
            {
                var dodgeLead = context.ByName(villageId, VillageSettingEnums.DodgeSendSecondsBeforeImpact);
                if (dodgeLead <= 0) dodgeLead = 30;
                reserve = dodgeLead + DodgeMarginSeconds;
            }
            return attackSeconds - reserve;
        }

        private static async ValueTask<Result> HandleAsync(
            Task task,
            AppDbContext context,
            IChromeBrowser browser,
            ToRallyPointOverviewCommand.Handler toOverviewCommand,
            TrainCheapestTroopCommand.Handler trainCheapestTroopCommand,
            ToSendResourcePageCommand.Handler toSendResourcePageCommand,
            SendResourceCommand.Handler sendResourceCommand,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            var overviewResult = await toOverviewCommand.HandleAsync(new(task.VillageId), cancellationToken);
            if (overviewResult.IsFailed)
            {
                if (overviewResult.HasError<MissingBuilding>())
                {
                    logger.Warning("No rally point in {VillageId}, cannot read the incoming attack.", task.VillageId);
                    return Skip.Error.WithErrors(overviewResult.Errors);
                }
                return overviewResult;
            }

            var attackSeconds = RallyPointOverviewParser.GetIncomingAttackSeconds(browser.Html);
            if (attackSeconds is null)
            {
                // No incoming attack showing right now (already landed, resolved, or the
                // IsUnderAttack flag was stale) - nothing to do, and nothing marked as handled.
                AttackWakeWindow.ReleaseReservation(task.AccountId, task.VillageId);
                return Skip.Error;
            }

            // 2026-10-19: the 3-5 minutes before impact belong to the defensive tasks.
            AttackWakeWindow.Reserve(task.AccountId, task.VillageId, attackSeconds.Value, logger);

            // From here on this attack is handled - success, failure or skip alike.
            var now = DateTime.Now;
            DefendAttackMemory.MarkHandled(task.VillageId.Value, now.AddSeconds(attackSeconds.Value + HandledGraceSeconds), now);

            var budgetSeconds = GetBudgetSeconds(context, task.VillageId, attackSeconds.Value);
            if (budgetSeconds < MinimumActionTimeSeconds)
            {
                logger.Warning(
                    "Incoming attack on {VillageId} lands in {Seconds}s - only {Budget}s would be left before Dodge, skipping defend so Dodge is not blocked.",
                    task.VillageId, attackSeconds.Value, budgetSeconds);
                return Skip.Error;
            }

            logger.Information(
                "Incoming attack on {VillageId} lands in {Seconds}s - defending now (budget {Budget}s, once for this attack).",
                task.VillageId, attackSeconds.Value, budgetSeconds);

            using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            budget.CancelAfter(TimeSpan.FromSeconds(budgetSeconds));

            try
            {
                return await RunAsync(
                    task, context, browser,
                    trainCheapestTroopCommand, toSendResourcePageCommand, sendResourceCommand,
                    logger, budget.Token, cancellationToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                logger.Warning("Defend ran out of its {Budget}s budget in {VillageId}, stopping so Dodge is not blocked.", budgetSeconds, task.VillageId);
                return Skip.Error;
            }
        }

        private static async ValueTask<Result> RunAsync(
            Task task,
            AppDbContext context,
            IChromeBrowser browser,
            TrainCheapestTroopCommand.Handler trainCheapestTroopCommand,
            ToSendResourcePageCommand.Handler toSendResourcePageCommand,
            SendResourceCommand.Handler sendResourceCommand,
            ILogger logger,
            CancellationToken token,
            CancellationToken pauseToken)
        {
            // 1) Train the cheapest troop with the whole stock. A failure here must NOT stop the
            //    evacuation below - the resources are the thing worth saving.
            var storage = context.Storages.FirstOrDefault(x => x.VillageId == task.VillageId.Value);
            var trained = 0;
            if (storage is null)
            {
                logger.Warning("No known stock for {VillageId} yet, skipping the training step.", task.VillageId);
            }
            else
            {
                var trainResult = await trainCheapestTroopCommand.HandleAsync(
                    new(task.VillageId, storage.Wood, storage.Clay, storage.Iron, storage.Crop), token);
                if (trainResult.IsFailed)
                {
                    if (token.IsCancellationRequested) return Abort(pauseToken, trainResult.Errors, logger);
                    logger.Warning("Training failed in {VillageId}, still sending the resources away: {Errors}",
                        task.VillageId, string.Join(" | ", trainResult.Errors.Select(e => e.Message)));
                }
                else
                {
                    trained = trainResult.Value;
                }
            }

            // 2) Whatever is left, read live off the page we are on (the resource bar is on every
            //    in-game page) instead of tracking what training consumed.
            var stock = new DefendResources(
                StorageParser.GetWood(browser.Html),
                StorageParser.GetClay(browser.Html),
                StorageParser.GetIron(browser.Html),
                StorageParser.GetCrop(browser.Html));
            if (stock.Total <= 0)
            {
                logger.Information("Nothing left to send away in {VillageId} (trained {Trained}).", task.VillageId, trained);
                return Result.Ok();
            }

            // 3) Marketplace + merchants. None -> skip the sending, nothing else to do.
            var pageResult = await toSendResourcePageCommand.HandleAsync(new(task.VillageId), token);
            if (pageResult.IsFailed)
            {
                if (pageResult.HasError<MissingBuilding>())
                {
                    logger.Warning("No marketplace in {VillageId}, cannot send resources away (trained {Trained}).", task.VillageId, trained);
                    return Skip.Error.WithErrors(pageResult.Errors);
                }
                return Abort(pauseToken, pageResult.Errors, logger);
            }

            var freeMerchants = SendResourceParser.GetFreeMerchants(browser.Html);
            if (freeMerchants <= 0)
            {
                logger.Information("No free merchants in {VillageId}, skipping the resource send (trained {Trained}).", task.VillageId, trained);
                return Result.Ok();
            }

            var capacity = SendResourceParser.GetMerchantCapacity(browser.Html);
            if (capacity <= 0) capacity = 1;
            var send = DefendResourceTargetRules.ScaleToCapacity(stock, (long)freeMerchants * capacity);
            if (send.Total <= 0) return Result.Ok();

            // 4) Pick the receiving village: random, preferring one with room, avoiding villages
            //    that are under attack themselves.
            var others = context.Villages
                .Where(v => v.AccountId == task.AccountId.Value && v.Id != task.VillageId.Value)
                .Select(v => new { v.Id, v.IsUnderAttack })
                .ToList();
            var otherIds = others.Select(v => v.Id).ToList();
            var storages = context.Storages.Where(s => otherIds.Contains(s.VillageId)).ToList();

            var candidates = others
                .Select(v =>
                {
                    var s = storages.FirstOrDefault(x => x.VillageId == v.Id);
                    var stockOfTarget = s is null
                        ? new DefendResources(0, 0, 0, 0)
                        : new DefendResources(s.Wood, s.Clay, s.Iron, s.Crop);
                    return new DefendTargetCandidate(v.Id, stockOfTarget, s?.Warehouse ?? 0, s?.Granary ?? 0, v.IsUnderAttack);
                })
                .ToList();

            var targetId = DefendResourceTargetRules.ChooseTarget(candidates, send, Random.Shared);
            if (targetId is null)
            {
                logger.Warning("No other village to send {VillageId}'s resources to.", task.VillageId);
                return Result.Ok();
            }

            var amounts = new Dictionary<string, long>();
            if (send.Wood > 0) amounts["wood"] = send.Wood;
            if (send.Clay > 0) amounts["clay"] = send.Clay;
            if (send.Iron > 0) amounts["iron"] = send.Iron;
            if (send.Crop > 0) amounts["crop"] = send.Crop;

            var sendResult = await sendResourceCommand.HandleAsync(
                new(task.VillageId, new VillageId(targetId.Value), amounts), token);
            if (sendResult.IsFailed) return Abort(pauseToken, sendResult.Errors, logger);

            logger.Information(
                "Defend done for {VillageId}: trained {Trained} troop(s), sent {Plan} to village {Target}.",
                task.VillageId, trained,
                string.Join(", ", amounts.Select(x => $"{x.Key}={x.Value}")), targetId.Value);

            return Result.Ok();
        }
    }
}
