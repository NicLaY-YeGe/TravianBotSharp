using MainCore.Commands.Features.Scheduling;
using MainCore.Commands.Features.Trap;
using MainCore.Commands.NextExecute;
using MainCore.Tasks.Base;

namespace MainCore.Tasks
{
    // 2026-10-25, user decision: like troop training, trap building waits for the game-side queue
    // instead of coming back every few minutes (see QueueDeferRules):
    //   - Trapper at its level's cap: nothing to build until the building levels up or traps are
    //     used up -> next visit in about two hours (the 2026-10-24 log showed a visit every ~14
    //     minutes that only logged "already at its cap (400 traps)").
    //   - A trap batch is still being built: no new batch, next visit at its end + a small buffer.
    //   - Otherwise (a batch was just started and its countdown is read, or resources are short /
    //     nothing is readable) the user's normal repeat time applies to what is not known.
    // The queue countdown is read from `table.under_progress` (TrainQueueParser); that markup has
    // NOT been checked against a capture of an ACTIVE trap queue yet - when it does not match, the
    // behaviour falls back to the normal repeat time (the old behaviour), waits are capped at 3 h.
    [Handler]
    public static partial class TrapTask
    {
        public sealed class Task : VillageTask
        {
            public Task(AccountId accountId, VillageId villageId) : base(accountId, villageId)
            {
            }

            protected override string TaskName => "Build traps";

            public override bool CanStart(AppDbContext context)
            {
                return context.BooleanByName(VillageId, VillageSettingEnums.TrapEnable);
            }
        }

        private static async ValueTask<Result> HandleAsync(
            Task task,
            IChromeBrowser browser,
            ToTrapPageCommand.Handler toTrapPageCommand,
            BuildTrapsCommand.Handler buildTrapsCommand,
            NextExecuteTrapTaskCommand.Handler nextExecuteTrapTaskCommand,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            var result = await toTrapPageCommand.HandleAsync(new(task.VillageId), cancellationToken);
            if (result.IsFailed)
            {
                if (result.HasError<MissingBuilding>())
                {
                    // No Trapper in this village (Trapper is Gaul-only, or it just hasn't been
                    // built yet) - nothing to retry right now. Quietly skip this cycle rather
                    // than failing the whole task; if a Trapper appears later (tribe/building
                    // change) the next cycle will pick it up on its own.
                    logger.Warning("No Trapper here, skipping trap build this cycle.");
                    await nextExecuteTrapTaskCommand.HandleAsync(new(task), cancellationToken);
                    return Result.Ok();
                }
                return result;
            }

            var maxPossible = TrapParser.GetMaxPossibleTraps(browser.Html);
            var currentTraps = TrapParser.GetCurrentTrapCount(browser.Html);
            var atCap = QueueDeferRules.IsTrapperAtCap(maxPossible, currentTraps);

            int? queueSeconds = null;
            if (atCap)
            {
                logger.Information("Trapper in {VillageId} is at its level's cap ({Current}/{Max} traps) - nothing to build.", task.VillageId, currentTraps, maxPossible);
            }
            else
            {
                queueSeconds = TrainQueueParser.GetSecondsRemaining(browser.Html);
                if (queueSeconds is not null)
                {
                    logger.Information(
                        "Trapper in {VillageId}: a trap batch is still being built ({Seconds}s left) - no new batch now.",
                        task.VillageId, queueSeconds.Value);
                }
                else
                {
                    result = await buildTrapsCommand.HandleAsync(new(task.VillageId), cancellationToken);
                    if (result.IsFailed && !result.HasError<MissingResource>())
                    {
                        return result;
                    }
                    // A MissingResource here (per BuildTrapsCommand: either genuinely 0 affordable, or
                    // TrainWhenLowResource is off and the target batch doesn't fit) is not a task
                    // failure - same philosophy as TrainTroopTask - just try again next cycle.

                    // maxPossible == 0 means the page was not read properly: the command did not build
                    // anything then, so there is no queue to wait for either.
                    if (result.IsSuccess && maxPossible > 0)
                    {
                        queueSeconds = await QueueWait.ReadSecondsAfterActionAsync(browser, cancellationToken);
                    }
                }
            }

            await nextExecuteTrapTaskCommand.HandleAsync(new(task), cancellationToken);

            // NextExecute has just set ExecuteAt to now + the user's repeat time; the cap / queue-aware
            // wait replaces it only when it differs.
            var normalSeconds = Math.Max(1, (int)Math.Round((task.ExecuteAt - DateTime.Now).TotalSeconds));
            var nextSeconds = QueueDeferRules.NextTrapSeconds(queueSeconds, atCap, normalSeconds, Random.Shared);
            if (nextSeconds != normalSeconds)
            {
                task.ExecuteAt = DateTime.Now.AddSeconds(nextSeconds);
                logger.Information(
                    "Trapper in {VillageId}: next visit at {Time:HH:mm:ss} (the normal repeat time would be {Normal}s).",
                    task.VillageId, task.ExecuteAt, normalSeconds);
            }

            return Result.Ok();
        }
    }
}
