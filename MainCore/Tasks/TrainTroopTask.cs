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
    //
    // 2026-10-26, user decisions (live log 2026-10-26: 11.7k Phalanx in 7 h, 209 visits, no building upgraded):
    //   - Every building has its own "not again before" interval (BarrackRepeatTime..., 0/0 = the general
    //     TrainTroopRepeatTime). After a batch the next visit is the queue end + buffer but NEVER earlier
    //     than that interval, so a short queue no longer brings the task back every 1-2 minutes. The
    //     per-building "not before" is kept in TrainTroopSchedule; the task wakes for the earliest one.
    //   - "Construction first" (ConstructionFirst setting): while UpgradeBuildingTask reports a plan waiting
    //     for resources (ConstructionHold) this village trains nothing.
    // The queue countdown is read from `table.under_progress` (TrainQueueParser); the markup was
    // checked against a REAL active Barracks queue (2026-10-25; the timers are cumulative, the last
    // batch's timer is the queue end). If it ever does not match, nothing is read and every path
    // above falls back to the normal repeat time - i.e. the old behaviour. Waits taken from the page are capped at 3 hours (QueueDeferRules).
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
            AppDbContext context,
            IChromeBrowser browser,
            GetTrainTroopBuildingCommand.Handler getTrainTroopBuildingQuery,
            ToTrainTroopPageCommand.Handler toTrainTroopPageCommand,
            TrainTroopCommand.Handler trainTroopCommand,
            SaveVillageSettingCommand.Handler saveVillageSettingCommand,
            NextExecuteTrainTroopTaskCommand.Handler nextExecuteTrainTroopTaskCommand,
            ISettingService settingService,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            if (context.BooleanByName(task.VillageId, VillageSettingEnums.ConstructionFirst))
            {
                var holdUntil = ConstructionHold.GetUntil(task.VillageId);
                if (holdUntil is not null)
                {
                    task.ExecuteAt = holdUntil.Value.AddSeconds(Random.Shared.Next(QueueDeferRules.BufferMinSeconds, QueueDeferRules.BufferMaxSeconds + 1));
                    logger.Information(
                        "Train troop in {VillageId}: construction first - a building is waiting for resources, no training until {Time:HH:mm:ss}.",
                        task.VillageId, task.ExecuteAt);
                    return Result.Ok();
                }
            }

            Result result;
            var buildings = await getTrainTroopBuildingQuery.HandleAsync(new(task.VillageId), cancellationToken);
            var settings = new Dictionary<VillageSettingEnums, int>();

            // The moment each building may be visited again; the task comes back for the earliest.
            var nextTimes = new List<DateTime>();

            foreach (var building in buildings)
            {
                if (cancellationToken.IsCancellationRequested) return Cancel.Error;

                var notBefore = TrainTroopSchedule.GetNotBefore(task.VillageId, building);
                if (notBefore > DateTime.Now)
                {
                    nextTimes.Add(notBefore);
                    continue;
                }

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

                var intervalSeconds = GetIntervalSeconds(settingService, task.VillageId, building);

                var runningSeconds = TrainQueueParser.GetSecondsRemaining(browser.Html);
                if (runningSeconds is not null)
                {
                    logger.Information(
                        "Train troop in {VillageId}: the {Building} queue is busy for another {Seconds}s ({Timers} timer(s) on the page) - no new batch now.",
                        task.VillageId, building, runningSeconds.Value, TrainQueueParser.CountTimers(browser.Html));
                    var busyNext = DateTime.Now.AddSeconds(QueueDeferRules.NextBuildingSeconds(false, runningSeconds, intervalSeconds, Random.Shared));
                    TrainTroopSchedule.Set(task.VillageId, building, busyNext);
                    nextTimes.Add(busyNext);
                    continue;
                }

                result = await trainTroopCommand.HandleAsync(new(task.VillageId, building), cancellationToken);
                if (result.IsFailed)
                {
                    if (result.HasError<MissingResource>())
                    {
                        // Other buildings train other troops - keep going; this one retries after its interval.
                        var starvedNext = DateTime.Now.AddSeconds(QueueDeferRules.NextBuildingSeconds(false, null, intervalSeconds, Random.Shared));
                        TrainTroopSchedule.Set(task.VillageId, building, starvedNext);
                        nextTimes.Add(starvedNext);
                        continue;
                    }
                    return result;
                }

                var queueSeconds = await QueueWait.ReadSecondsAfterActionAsync(browser, cancellationToken);
                var trainedNext = DateTime.Now.AddSeconds(QueueDeferRules.NextBuildingSeconds(true, queueSeconds, intervalSeconds, Random.Shared));
                TrainTroopSchedule.Set(task.VillageId, building, trainedNext);
                nextTimes.Add(trainedNext);
            }

            await saveVillageSettingCommand.HandleAsync(new(task.AccountId, task.VillageId, settings), cancellationToken);

            if (nextTimes.Count == 0)
            {
                // No usable building: keep the general repeat time.
                await nextExecuteTrainTroopTaskCommand.HandleAsync(new(task), cancellationToken);
                return Result.Ok();
            }

            var next = nextTimes.Min();
            var floor = DateTime.Now.AddSeconds(5);
            task.ExecuteAt = next < floor ? floor : next;
            logger.Information(
                "Train troop in {VillageId}: next visit at {Time:HH:mm:ss} (each building waits for its own interval, queue end plus a small buffer at the earliest).",
                task.VillageId, task.ExecuteAt);

            return Result.Ok();
        }

        // The building's own interval (minutes in the settings); 0/0 falls back to the general one.
        private static int GetIntervalSeconds(ISettingService settingService, VillageId villageId, BuildingEnums building)
        {
            var (minSetting, maxSetting) = TrainTroopCommand.RepeatSettings[building];
            var min = settingService.ByName(villageId, minSetting);
            var max = settingService.ByName(villageId, maxSetting);
            if (min <= 0 && max <= 0)
            {
                min = settingService.ByName(villageId, VillageSettingEnums.TrainTroopRepeatTimeMin);
                max = settingService.ByName(villageId, VillageSettingEnums.TrainTroopRepeatTimeMax);
            }

            if (max < min) (min, max) = (max, min);
            var seconds = Random.Shared.Next(Math.Max(0, min) * 60, Math.Max(0, max) * 60 + 1);
            return Math.Max(60, seconds);
        }
    }
}
