using MainCore.Commands.Features.DefendDonate;
using MainCore.Commands.Features.DodgeTroop;
using MainCore.Enums;
using MainCore.Parsers;
using MainCore.Tasks.Base;

namespace MainCore.Tasks
{
    // Reworked again 2026-10-22 per explicit user decision (history: 09-27 train + donate to the
    // alliance, which hung 180 s on the Alliance Bonus page and pushed Dodge past impact; 10-19 train
    // + send the rest to another own village by Marketplace, which in 3 live attacks never ran
    // because "No free merchants" - CHANGELOG.md). The "Donate" in the name is historical no more:
    // donating is back, the village setting / task class keep their names so saved settings and the
    // dispatch in CustomServiceScopeFactory stay valid.
    //
    // Behaviour, ONCE per incoming attack, right when the attack is seen:
    //   1. train the tribe's cheapest troop (T1 infantry, Barracks) with the village's whole stock;
    //   2. donate EVERYTHING that is left to the alliance's Recruitment (troop production) bonus
    //      (DonateAllianceBonusCommand). If the daily donation limit is used up the rest simply stays
    //      in the village - no merchant fallback (user decision);
    //   3. never run again for this attack, whatever the outcome (DefendAttackMemory).
    // Top priority is that Dodge still works: the whole sequence runs under a hard time budget
    // that ends before Dodge's send moment, the donation has its own cap on top of that, and every
    // failure is Skip (never Retry/Stop, which would hold the single-threaded queue or pause the
    // account - CLAUDE.md 2l).
    [Handler]
    public static partial class DefendDonateTask
    {
        public sealed class Task : VillageTask
        {
            public Task(AccountId accountId, VillageId villageId) : base(accountId, villageId)
            {
            }

            protected override string TaskName => "Defend: train + donate";

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

        // Own cap for the donation step (it is the last step, so it can only ever eat its own time).
        // The 2026-09-27 version hung 180 s here.
        private const int DonationTimeoutSeconds = 60;

        // Amount passed for a resource whose value could not be read (the donation command caps it at the page maximum).
        private const long AskForAllAmount = 1_000_000_000;

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
        private static int GetBudgetSeconds(AppDbContext context, VillageId villageId, int attackSeconds, bool dodgeWillRun)
        {
            var reserve = ReserveBeforeImpactSeconds;
            if (dodgeWillRun)
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
            DonateAllianceBonusCommand.Handler donateAllianceBonusCommand,
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

            // 2026-10-23: Defend reacts to attacks AND raids (both hit the stock and the garrison).
            var movements = RallyPointOverviewParser.GetIncomingMovements(browser.Html);
            var first = movements.OrderBy(x => x.Seconds).FirstOrDefault();
            if (first is null)
            {
                // No incoming attack or raid showing right now (already landed, resolved, or the
                // IsUnderAttack flag was stale) - nothing to do, and nothing marked as handled.
                // Logged instead of skipping silently (2026-10-22 log: 14 silent skips).
                AttackWakeWindow.ReleaseReservation(task.AccountId, task.VillageId);
                await IncomingMovementDiagnostics.ReportNothingToDoAsync(browser, logger, task.VillageId, "Defend", movements);
                return Skip.Error;
            }
            var attackSeconds = (int?)first.Seconds;

            // 2026-10-19: the 3-5 minutes before impact belong to the defensive tasks.
            AttackWakeWindow.Reserve(task.AccountId, task.VillageId, attackSeconds.Value, logger);

            // From here on this attack is handled - success, failure or skip alike.
            var now = DateTime.Now;
            DefendAttackMemory.MarkHandled(task.VillageId.Value, now.AddSeconds(attackSeconds.Value + HandledGraceSeconds), now);

            // Dodge only holds its send moment for this movement if it will actually react to it.
            var dodgeWillRun = context.BooleanByName(task.VillageId, VillageSettingEnums.DodgeEnable)
                && (first.Kind == RallyPointOverviewParser.IncomingKind.Attack
                    || context.BooleanByName(task.VillageId, VillageSettingEnums.DodgeRaidEnable));
            var budgetSeconds = GetBudgetSeconds(context, task.VillageId, attackSeconds.Value, dodgeWillRun);
            if (budgetSeconds < MinimumActionTimeSeconds)
            {
                logger.Warning(
                    "Incoming {Kind} on {VillageId} lands in {Seconds}s - only {Budget}s would be left before Dodge, skipping defend so Dodge is not blocked.",
                    first.Kind.ToString().ToLowerInvariant(), task.VillageId, attackSeconds.Value, budgetSeconds);
                return Skip.Error;
            }

            logger.Information(
                "Incoming {Kind} on {VillageId} lands in {Seconds}s - defending now (budget {Budget}s, once for this movement).",
                first.Kind.ToString().ToLowerInvariant(), task.VillageId, attackSeconds.Value, budgetSeconds);

            using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            budget.CancelAfter(TimeSpan.FromSeconds(budgetSeconds));

            try
            {
                return await RunAsync(
                    task, context, browser,
                    trainCheapestTroopCommand, donateAllianceBonusCommand,
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
            DonateAllianceBonusCommand.Handler donateAllianceBonusCommand,
            ILogger logger,
            CancellationToken token,
            CancellationToken pauseToken)
        {
            // 1) Train the cheapest troop with the whole stock. A failure here must NOT stop the
            //    donation below.
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
                    logger.Warning("Training failed in {VillageId}, still donating what is there: {Errors}",
                        task.VillageId, string.Join(" | ", trainResult.Errors.Select(e => e.Message)));
                }
                else
                {
                    trained = trainResult.Value;
                }
            }

            // 2) Whatever is left, read live off the page we are on (the resource bar is on every
            //    in-game page) instead of tracking what training consumed.
            // 2026-10-23 live log: Wood came back as -1 on BOTH defends (clay/iron/crop were fine), so
            // ~3.8k wood stayed in the village and the log said "donated 11465 of 11464". The parser
            // returns -1 when it cannot read a value. An unreadable value is therefore asked for as
            // "all" (AskForAllAmount): the donation command caps every resource at the alliance page's own
            // maximum, which is the real stock, so nothing can be over-donated.
            var rawStock = new[]
            {
                StorageParser.GetWood(browser.Html),
                StorageParser.GetClay(browser.Html),
                StorageParser.GetIron(browser.Html),
                StorageParser.GetCrop(browser.Html),
            };
            var unreadable = rawStock.Count(x => x < 0);
            if (unreadable > 0)
            {
                logger.Warning("Defend in {VillageId}: {Count} resource value(s) could not be read from the page ({Values}), donating those as 'all' (capped by the alliance page).",
                    task.VillageId, unreadable, string.Join("/", rawStock));
            }
            var stock = new DefendResources(
                rawStock[0] < 0 ? AskForAllAmount : rawStock[0],
                rawStock[1] < 0 ? AskForAllAmount : rawStock[1],
                rawStock[2] < 0 ? AskForAllAmount : rawStock[2],
                rawStock[3] < 0 ? AskForAllAmount : rawStock[3]);
            if (stock.Total <= 0)
            {
                logger.Information("Nothing left to donate in {VillageId} (trained {Trained}).", task.VillageId, trained);
                return Result.Ok();
            }

            // 3) Donate all of it to the alliance's Recruitment bonus. The command caps each resource at
            //    the page's own maximum (= this village's stock) and at the daily limit; anything it
            //    cannot donate stays in the village.
            var donateResult = await donateAllianceBonusCommand.HandleAsync(
                new(task.VillageId, stock.Wood, stock.Clay, stock.Iron, stock.Crop, DonationTimeoutSeconds), token);
            if (donateResult.IsFailed) return Abort(pauseToken, donateResult.Errors, logger);

            logger.Information(
                "Defend done for {VillageId}: trained {Trained} troop(s), donated {Donated} resources to the alliance Recruitment bonus{Note}.",
                task.VillageId, trained, donateResult.Value,
                unreadable > 0 || donateResult.Value >= stock.Total ? "" : " (the rest stays in the village)");

            return Result.Ok();
        }
    }
}
