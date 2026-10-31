using MainCore.Commands.Features.NpcResource;
using MainCore.Commands.Features.Scheduling;
using MainCore.Commands.Features.TrainTroop;
using MainCore.Commands.Features.UseHeroItem;
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
    //
    // 2026-10-30, user decision ("kisla ve ahirda asker hic bos kalmasin"): QUEUE HORIZON per building
    // (<Building>HorizonMin / HorizonTarget, minutes; 0/0 = off = everything above). With a horizon the
    // building is NOT left to run empty: the queue is topped up to "fill up to" minutes at every visit, the
    // visit is planned for the moment the queue has "at least" minutes left (QueueHorizonRules), and shortly
    // before the account's offline hours (OnlineHoursMask) the queue is filled so it lasts until the bot works
    // again, up to TrainOfflineCoverMaxMinutes. A busy queue is topped up, not skipped. The per-troop "not again
    // before" interval is not used for such a building (the horizon decides the visits); a troop's "amount max"
    // is the most units ONE visit may add. When a batch costs more than the village holds the difference is made
    // up by an NPC trade (TrainUseNpc, gold) and then from the hero's inventory (TrainUseHeroResource, never below
    // HeroResourceReserve); what still cannot be paid is trained partially.
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
            ToNpcResourcePageCommand.Handler toNpcResourcePageCommand,
            NpcExchangeToTargetCommand.Handler npcExchangeToTargetCommand,
            UseHeroResourceCommand.Handler useHeroResourceCommand,
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

                // 2026-10-30: queue horizon on: this building is handled by the horizon flow below.
                var horizon = GetHorizon(settingService, task.VillageId, building);
                if (horizon.Enabled)
                {
                    if (plans.Count == 0) continue;

                    var horizonResult = await TrainWithHorizon(
                        task, building, plans, horizon, nextTimes, settings,
                        context, browser, settingService, logger,
                        toTrainTroopPageCommand, trainTroopCommand, toNpcResourcePageCommand, npcExchangeToTargetCommand, useHeroResourceCommand,
                        cancellationToken);
                    if (horizonResult.IsFailed) return horizonResult;
                    continue;
                }

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

        private static QueueHorizon GetHorizon(ISettingService settingService, VillageId villageId, BuildingEnums building)
        {
            if (!TrainTroopCommand.HorizonSettings.TryGetValue(building, out var keys)) return default;
            return QueueHorizon.FromMinutes(settingService.ByName(villageId, keys.Item1), settingService.ByName(villageId, keys.Item2));
        }

        // One visit to a building that has a queue horizon: top the queue up to the horizon, then plan the next visit.
        private static async ValueTask<Result> TrainWithHorizon(
            Task task,
            BuildingEnums building,
            List<TroopPlan> plans,
            QueueHorizon horizon,
            List<DateTime> nextTimes,
            Dictionary<VillageSettingEnums, int> settings,
            AppDbContext context,
            IChromeBrowser browser,
            ISettingService settingService,
            ILogger logger,
            ToTrainTroopPageCommand.Handler toTrainTroopPageCommand,
            TrainTroopCommand.Handler trainTroopCommand,
            ToNpcResourcePageCommand.Handler toNpcResourcePageCommand,
            NpcExchangeToTargetCommand.Handler npcExchangeToTargetCommand,
            UseHeroResourceCommand.Handler useHeroResourceCommand,
            CancellationToken cancellationToken)
        {
            // The building as a whole has one "not before" (TroopEnums.None).
            var notBefore = TrainTroopSchedule.GetNotBefore(task.VillageId, building);
            if (notBefore > DateTime.Now)
            {
                nextTimes.Add(notBefore);
                return Result.Ok();
            }

            var result = await toTrainTroopPageCommand.HandleAsync(new(task.VillageId, building), cancellationToken);
            if (result.IsFailed)
            {
                if (result.HasError<MissingBuilding>())
                {
                    logger.Warning("Disable train troop on this building.", building);
                    DisableBuilding(settings, building);
                    return Result.Ok();
                }
                return result;
            }

            var now = DateTime.Now;
            var queueSeconds = TrainQueueParser.GetSecondsRemaining(browser.Html) ?? 0;

            // Online hours: when does the bot stop working and when is it back (null = no gap to plan for).
            var mask = settingService.ByName(task.AccountId, AccountSettingEnums.OnlineHoursMask);
            var offlineCapSeconds = settingService.ByName(task.AccountId, AccountSettingEnums.TrainOfflineCoverMaxMinutes) * 60;
            var offlineStart = OnlineHoursRules.NextOfflineStart(mask, now);
            DateTime? onlineStart = offlineStart is { } offline ? OnlineHoursRules.NextOnlineStart(mask, offline) : null;

            var fillTo = QueueHorizonRules.FillToSeconds(now, horizon, offlineStart, onlineStart, offlineCapSeconds);
            var queueNow = queueSeconds;
            var trainedAny = false;

            logger.Information(
                "Train troop in {VillageId}: {Building} queue horizon - queue holds {Queue} min, fill up to {FillTo} min (at least {Min} min, target {Target} min).",
                task.VillageId, building, queueSeconds / 60, fillTo / 60, horizon.MinSeconds / 60, horizon.TargetSeconds / 60);

            foreach (var plan in plans)
            {
                if (cancellationToken.IsCancellationRequested) return Cancel.Error;

                var need = QueueHorizonRules.NeedSeconds(fillTo, queueNow);
                if (need <= 0) break;

                // After a batch the page is no longer a clean training form: open it again for the next troop.
                if (trainedAny)
                {
                    result = await toTrainTroopPageCommand.HandleAsync(new(task.VillageId, building), cancellationToken);
                    if (result.IsFailed) return result;
                }

                var unitSeconds = TrainTroopParser.GetUnitSeconds(browser.Html, plan.Troop);
                int units;
                if (unitSeconds > 0)
                {
                    units = QueueHorizonRules.UnitsFor(need, unitSeconds, plan.AmountMax);
                }
                else
                {
                    units = TrainTroopPlanner.PickAmount(plan, Random.Shared);
                    logger.Warning("Train troop in {VillageId}: the training time of {Troop} could not be read, using the plain amount {Units}.", task.VillageId, plan.Troop, units);
                }
                if (units <= 0) continue;

                var (makeUpResult, navigated) = await MakeUpResources(
                    task, plan, units, context, browser, settingService, logger,
                    toNpcResourcePageCommand, npcExchangeToTargetCommand, useHeroResourceCommand, cancellationToken);
                if (makeUpResult.IsFailed) return makeUpResult;

                // The NPC page / hero inventory replaced the training page: go back before typing the amount.
                if (navigated)
                {
                    result = await toTrainTroopPageCommand.HandleAsync(new(task.VillageId, building), cancellationToken);
                    if (result.IsFailed) return result;
                }

                var trainResult = await trainTroopCommand.HandleAsync(new(task.VillageId, building, plan, units), cancellationToken);
                if (trainResult.IsFailed)
                {
                    if (trainResult.HasError<MissingResource>())
                    {
                        logger.Information("Train troop in {VillageId}: not enough resources for {Troop} in {Building} (wanted {Units}).", task.VillageId, plan.Troop, building, units);
                        continue;
                    }
                    return trainResult;
                }

                trainedAny = true;
                var after = await QueueWait.ReadSecondsAfterActionAsync(browser, cancellationToken);
                queueNow = after ?? queueNow + units * Math.Max(0, unitSeconds);
            }

            // Next visit: when the queue has "at least" minutes left. A target that could not be reached (resources,
            // per-visit cap) is looked at again sooner, so the queue is filled as soon as the village can pay.
            var next = QueueHorizonRules.NextVisit(DateTime.Now, queueNow, horizon.MinSeconds, offlineStart);
            if (QueueHorizonRules.NeedSeconds(fillTo, queueNow) > 0)
            {
                var retry = DateTime.Now.AddSeconds(QueueHorizonRules.StarvedRetrySeconds(Random.Shared));
                if (retry < next) next = retry;
            }

            TrainTroopSchedule.Set(task.VillageId, building, next);
            nextTimes.Add(next);
            return Result.Ok();
        }

        // A batch that costs more than the village holds: first an NPC trade (when it can cover the deficit by
        // redistributing the village's own total), then the hero's inventory (above the reserve). Returns whether
        // the browser left the training page (the caller then opens it again). A failure here is never fatal: the
        // caller trains what the village can pay.
        private static async ValueTask<(Result Result, bool Navigated)> MakeUpResources(
            Task task,
            TroopPlan plan,
            int units,
            AppDbContext context,
            IChromeBrowser browser,
            ISettingService settingService,
            ILogger logger,
            ToNpcResourcePageCommand.Handler toNpcResourcePageCommand,
            NpcExchangeToTargetCommand.Handler npcExchangeToTargetCommand,
            UseHeroResourceCommand.Handler useHeroResourceCommand,
            CancellationToken cancellationToken)
        {
            var useNpc = context.BooleanByName(task.VillageId, VillageSettingEnums.TrainUseNpc);
            var useHero = context.BooleanByName(task.VillageId, VillageSettingEnums.TrainUseHeroResource);
            if (!useNpc && !useHero) return (Result.Ok(), false);

            var html = browser.Html;
            if (TrainTroopParser.GetMaxAmount(html, plan.Troop) >= units) return (Result.Ok(), false);

            var (wood, clay, iron, crop) = TrainTroopParser.GetUnitCost(html, plan.Troop);
            long[] unitCost = [wood, clay, iron, crop];
            if (!TrainResourceRules.HasCost(unitCost))
            {
                logger.Warning("Train troop in {VillageId}: the unit cost of {Troop} could not be read, no resources are made up.", task.VillageId, plan.Troop);
                return (Result.Ok(), false);
            }

            var stock = ReadStock(html, context, task.VillageId);
            var needed = TrainResourceRules.Needed(unitCost, units);

            // Enough in the village: the page's own limit is something else (population, queue) - nothing to trade.
            if (!TrainResourceRules.AnyMissing(TrainResourceRules.Missing(needed, stock))) return (Result.Ok(), false);

            var navigated = false;

            if (useNpc)
            {
                var target = TrainResourceRules.NpcTarget(stock, needed);
                if (target is not null)
                {
                    navigated = true;
                    var pageResult = await toNpcResourcePageCommand.HandleAsync(new(task.VillageId), cancellationToken);
                    if (pageResult.HasError<Cancel>()) return (pageResult, true);
                    if (pageResult.IsSuccess)
                    {
                        var npcResult = await npcExchangeToTargetCommand.HandleAsync(new(task.VillageId, target), cancellationToken);
                        if (npcResult.HasError<Cancel>()) return (npcResult, true);
                        if (npcResult.IsSuccess)
                        {
                            logger.Information("Train troop in {VillageId}: NPC trade for {Units} x {Troop} done (wood/clay/iron/crop {Target}).", task.VillageId, units, plan.Troop, string.Join("/", target));
                            return (Result.Ok(), true);
                        }
                        logger.Warning("Train troop in {VillageId}: the NPC trade failed ({Reason}).", task.VillageId, string.Join("; ", npcResult.Reasons.Select(x => x.Message)));
                    }
                    else
                    {
                        logger.Warning("Train troop in {VillageId}: the NPC page could not be opened ({Reason}).", task.VillageId, string.Join("; ", pageResult.Reasons.Select(x => x.Message)));
                    }
                }
            }

            if (useHero)
            {
                var reserve = settingService.ByName(task.AccountId, AccountSettingEnums.HeroResourceReserve);
                var items = context.HeroItems.Where(x => x.AccountId == task.AccountId.Value).ToList();
                long StockOf(HeroItemEnums type) => items.Where(x => x.Type == type).Sum(x => (long)x.Amount);
                long[] heroStock = [StockOf(HeroItemEnums.Wood), StockOf(HeroItemEnums.Clay), StockOf(HeroItemEnums.Iron), StockOf(HeroItemEnums.Crop)];
                var available = TrainResourceRules.HeroAvailable(heroStock, reserve);

                var fit = TrainResourceRules.UnitsThatFit(units, unitCost, stock, available);
                var give = TrainResourceRules.Missing(TrainResourceRules.Needed(unitCost, fit), stock);
                if (fit > 0 && TrainResourceRules.AnyMissing(give))
                {
                    navigated = true;
                    var heroResult = await useHeroResourceCommand.HandleAsync(new(task.AccountId, task.VillageId, give), cancellationToken);
                    if (heroResult.HasError<Cancel>()) return (heroResult, true);
                    if (heroResult.IsSuccess)
                    {
                        logger.Information("Train troop in {VillageId}: took {Give} (wood/clay/iron/crop) from the hero's inventory for {Units} x {Troop}.", task.VillageId, string.Join("/", give), fit, plan.Troop);
                    }
                    else
                    {
                        logger.Warning("Train troop in {VillageId}: the hero's inventory could not be used ({Reason}).", task.VillageId, string.Join("; ", heroResult.Reasons.Select(x => x.Message)));
                    }
                }
                else if (fit <= 0)
                {
                    logger.Information("Train troop in {VillageId}: the hero's inventory (above the reserve {Reserve}) cannot cover {Troop}.", task.VillageId, reserve, plan.Troop);
                }
            }

            return (Result.Ok(), navigated);
        }

        // Wood / Clay / Iron / Crop of the village: the page's header values; a value the page could not give
        // (the wood node reads -1 on some training pages, TODO P1) falls back to the stored storage.
        private static long[] ReadStock(HtmlDocument html, AppDbContext context, VillageId villageId)
        {
            long[] stock = [StorageParser.GetWood(html), StorageParser.GetClay(html), StorageParser.GetIron(html), StorageParser.GetCrop(html)];
            if (stock.All(x => x >= 0)) return stock;

            var storage = context.Storages.FirstOrDefault(x => x.VillageId == villageId.Value);
            long[] stored = storage is null ? new long[] { 0, 0, 0, 0 } : new long[] { storage.Wood, storage.Clay, storage.Iron, storage.Crop };
            for (var i = 0; i < 4; i++)
            {
                if (stock[i] < 0) stock[i] = stored[i];
            }
            return stock;
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
