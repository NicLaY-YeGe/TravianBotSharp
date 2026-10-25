using MainCore.Commands.Features.Scheduling;
using MainCore.Commands.Features.TrainTroop;
using MainCore.Commands.NextExecute;
using MainCore.Commands.UI.Misc;
using MainCore.Tasks.Base;

namespace MainCore.Tasks
{
    // 2026-10-25, user decision: troop training waits for the END of the building's running queue
    // (plus a 30-60 s buffer) instead of coming back every few minutes and stacking another batch.
    //   - A building whose queue is still running gets no new batch; the task is moved to that end.
    //   - An idle building is trained as before; right after the click the queue's countdown is read
    //     (QueueWait) so the next visit is exactly at its end.
    //   - A building that could not be trained (not enough resources) or whose queue could not be
    //     read keeps the user's normal repeat time (TrainTroopRepeatTime), and the EARLIEST wait of
    //     all buildings wins - a busy Barracks never delays the retry of a starved Stable.
    // The queue countdown is read from `table.under_progress` (TrainQueueParser); that markup has
    // NOT been checked against a capture of an ACTIVE training queue yet. If it does not match,
    // nothing is read and every path above falls back to the normal repeat time - i.e. the old
    // behaviour. Waits taken from the page are capped at 3 hours (QueueDeferRules).
    [Handler]
    public static partial class TrainTroopTask
    {
        public sealed class Task : VillageTask
        {
            public Task(AccountId accountId, VillageId villageId) : base(accountId, villageId)
            {
            }

            protected override string TaskName => "Train troop";

            public override bool CanStart(AppDbContext context)
            {
                var settingEnable = context.BooleanByName(VillageId, VillageSettingEnums.TrainTroopEnable);
                if (!settingEnable) return false;
                return true;
            }
        }

        private static async ValueTask<Result> HandleAsync(
            Task task,
            IChromeBrowser browser,
            GetTrainTroopBuildingCommand.Handler getTrainTroopBuildingQuery,
            ToTrainTroopPageCommand.Handler toTrainTroopPageCommand,
            TrainTroopCommand.Handler trainTroopCommand,
            SaveVillageSettingCommand.Handler saveVillageSettingCommand,
            NextExecuteTrainTroopTaskCommand.Handler nextExecuteTrainTroopTaskCommand,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            Result result;
            var buildings = await getTrainTroopBuildingQuery.HandleAsync(new(task.VillageId), cancellationToken);
            var settings = new Dictionary<VillageSettingEnums, int>();

            // One entry per building that was looked at: seconds left on its queue, or null when
            // nothing is known (see QueueDeferRules.NextTrainSeconds).
            var queueSeconds = new List<int?>();

            foreach (var building in buildings)
            {
                if (cancellationToken.IsCancellationRequested) return Cancel.Error;

                result = await toTrainTroopPageCommand.HandleAsync(new(task.VillageId, building), cancellationToken);
                if (result.IsFailed)
                {
                    if (result.HasError<MissingBuilding>())
                    {
                        logger.Warning("Disable train troop on this building.", building);
                        settings.Add(TrainTroopCommand.TroopSettings[building], 0);
                        continue;
                    }
                    return result;
                }

                var runningSeconds = TrainQueueParser.GetSecondsRemaining(browser.Html);
                if (runningSeconds is not null)
                {
                    logger.Information(
                        "Train troop in {VillageId}: the {Building} queue is busy for another {Seconds}s ({Timers} timer(s) on the page) - no new batch now.",
                        task.VillageId, building, runningSeconds.Value, TrainQueueParser.CountTimers(browser.Html));
                    queueSeconds.Add(runningSeconds);
                    continue;
                }

                result = await trainTroopCommand.HandleAsync(new(task.VillageId, building), cancellationToken);
                if (result.IsFailed)
                {
                    if (result.HasError<MissingResource>())
                    {
                        queueSeconds.Add(null);
                        break;
                    }
                    return result;
                }

                queueSeconds.Add(await QueueWait.ReadSecondsAfterActionAsync(browser, cancellationToken));
            }

            await saveVillageSettingCommand.HandleAsync(new(task.AccountId, task.VillageId, settings), cancellationToken);
            await nextExecuteTrainTroopTaskCommand.HandleAsync(new(task), cancellationToken);

            // NextExecute has just set ExecuteAt to now + the user's repeat time; the queue-aware wait
            // replaces it only when it differs (a known queue end, earlier or later than that).
            var normalSeconds = Math.Max(1, (int)Math.Round((task.ExecuteAt - DateTime.Now).TotalSeconds));
            var nextSeconds = QueueDeferRules.NextTrainSeconds(queueSeconds, normalSeconds, Random.Shared);
            if (nextSeconds != normalSeconds)
            {
                task.ExecuteAt = DateTime.Now.AddSeconds(nextSeconds);
                logger.Information(
                    "Train troop in {VillageId}: next visit at {Time:HH:mm:ss} (queue end plus a small buffer; the normal repeat time would be {Normal}s).",
                    task.VillageId, task.ExecuteAt, normalSeconds);
            }

            return Result.Ok();
        }
    }
}
