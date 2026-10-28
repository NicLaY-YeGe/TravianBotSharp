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
    // 2026-10-27, user decision: every TROOP has its own amount (min-max) and interval (min-max minutes), set per
    // slot (TrainTroopSlots); the general next-visit row is gone from the UI (it remains the hidden fallback
    // for a troop whose interval is 0/0). A visit to a building trains every troop that is due; the
    // per-troop "not before" is kept in TrainTroopSchedule. The queue logic is unchanged.
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
            var tribe = (TribeEnums)settingService.ByName(task.VillageId, VillageSettingEnums.Tribe);

            // The moment each troop may be trained again; the task comes back for the earliest.
            var nextTimes = new List<DateTime>();

            foreach (var building in buildings)
            {
                if (cancellationToken.IsCancellationRequested) return Cancel.Error;

                var plans = TrainTroopPlanner.GetPlans(building, tribe, key => settingService.ByName(task.VillageId, key));
                var due = new List<TroopPlan>();
                foreach (var plan in plans)
                {
                    var notBefore = TrainTroopSchedule.GetNotBefore(task.VillageId, building, plan.Troop);
                    if (notBefore > DateTime.Now) nextTimes.Add(notBefore);
                    else due.Add(plan);
                }

                // Every troop of this building still has its own interval running.
                if (due.Count == 0) continue;

                result = await toTrainTroopPageCommand.HandleAsync(new(task.VillageId, building), cancellationToken);
                if (result.IsFailed)
                {
                    if (result.HasError<MissingBuilding>())
                    {
                        logger.Warning("Disable train troop on this building.", building);
                        DisableBuilding(settings, building);
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
                    foreach (var plan in due)
                    {
                        var intervalSeconds = GetIntervalSeconds(settingService, task.VillageId, building, plan);
                        var busyNext = DateTime.Now.AddSeconds(QueueDeferRules.NextBuildingSeconds(false, runningSeconds, intervalSeconds, Random.Shared));
                        TrainTroopSchedule.Set(task.VillageId, building, busyNext, plan.Troop);
                        nextTimes.Add(busyNext);
                    }
                    continue;
                }

                // One visit trains every troop that is due (cheaper ones may still fit when a dearer one is starved).
                var firstTroop = true;
                foreach (var plan in due)
                {
                    if (cancellationToken.IsCancellationRequested) return Cancel.Error;

                    // After a batch the page is no longer a clean training form: open it again for the next troop.
                    if (!firstTroop)
                    {
                        result = await toTrainTroopPageCommand.HandleAsync(new(task.VillageId, building), cancellationToken);
                        if (result.IsFailed) return result;
                    }
                    firstTroop = false;

                    var intervalSeconds = GetIntervalSeconds(settingService, task.VillageId, building, plan);
                    result = await trainTroopCommand.HandleAsync(new(task.VillageId, building, plan), cancellationToken);
                    if (result.IsFailed)
                    {
                        if (result.HasError<MissingResource>())
                        {
                            var starvedNext = DateTime.Now.AddSeconds(QueueDeferRules.NextBuildingSeconds(false, null, intervalSeconds, Random.Shared));
                            TrainTroopSchedule.Set(task.VillageId, building, starvedNext, plan.Troop);
                            nextTimes.Add(starvedNext);
                            continue;
                        }
                        return result;
                    }

                    var queueSeconds = await QueueWait.ReadSecondsAfterActionAsync(browser, cancellationToken);
                    var trainedNext = DateTime.Now.AddSeconds(QueueDeferRules.NextBuildingSeconds(true, queueSeconds, intervalSeconds, Random.Shared));
                    TrainTroopSchedule.Set(task.VillageId, building, trainedNext, plan.Troop);
                    nextTimes.Add(trainedNext);
                }
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

        // The troop's own interval (minutes in its slot); 0/0 falls back to the building's old interval, then the general one.
        private static int GetIntervalSeconds(ISettingService settingService, VillageId villageId, BuildingEnums building, TroopPlan plan)
        {
            var min = 0;
            var max = 0;
            if (TrainTroopCommand.RepeatSettings.TryGetValue(building, out var keys))
            {
                min = settingService.ByName(villageId, keys.Item1);
                max = settingService.ByName(villageId, keys.Item2);
            }

            if (min <= 0 && max <= 0)
            {
                min = settingService.ByName(villageId, VillageSettingEnums.TrainTroopRepeatTimeMin);
                max = settingService.ByName(villageId, VillageSettingEnums.TrainTroopRepeatTimeMax);
            }

            return TrainTroopPlanner.IntervalSeconds(plan, min, max, Random.Shared);
        }

        // MissingBuilding: switch the building's training off (every slot and the old single-troop setting).
        private static void DisableBuilding(Dictionary<VillageSettingEnums, int> settings, BuildingEnums building)
        {
            if (TrainTroopCommand.TroopSettings.TryGetValue(building, out var troopKey)) settings[troopKey] = 0;
            for (var slot = 1; slot <= TrainTroopSlots.SlotCount(building); slot++)
            {
                var (aMin, aMax, _, _) = TrainTroopSlots.Keys(building, slot);
                settings[aMin] = 0;
                settings[aMax] = 0;
            }
        }
    }
}
