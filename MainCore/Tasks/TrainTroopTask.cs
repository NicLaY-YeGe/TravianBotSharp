using MainCore.Commands.Features.NpcResource;
using MainCore.Commands.Features.Scheduling;
using MainCore.Commands.Features.TrainTroop;
using MainCore.Commands.Features.UseHeroItem;
using MainCore.Commands.Features.VillageOverview;
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
                // 2026-11-13: the share mode (ShareTrainTask) trains this village instead - the two modes exclude each other.
                if (context.BooleanByName(VillageId, VillageSettingEnums.TrainShareEnable)) return false;
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
            // 2026-11-13: CanStart is only checked when a task is queued, so a task that was already waiting when the user
            // switched to the share mode ends here (ExecuteAt unchanged = removed from the queue, rule 16, intended).
            if (context.BooleanByName(task.VillageId, VillageSettingEnums.TrainShareEnable)) return Result.Ok();

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

            // 2026-11-07: construction reserve (null = off). Training only spends the stock above this floor.
            var reserveFloor = GetReserveFloor(task, context, settingService, logger);

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
                        task, building, plans, horizon, nextTimes, settings, reserveFloor,
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
                    VillageOverviewStore.SetTrainQueue(task.VillageId.Value, (int)building, runningSeconds.Value);
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

                    // 2026-11-07: construction reserve - train only what the stock above the floor pays for.
                    var reserveAmount = 0;
                    if (reserveFloor is not null)
                    {
                        var wanted = TrainTroopPlanner.PickAmount(plan, Random.Shared);
                        var allowed = ReserveUnits(browser, context, task.VillageId, plan.Troop, wanted, reserveFloor, logger);
                        if (allowed <= 0 || (allowed < wanted && !context.BooleanByName(task.VillageId, VillageSettingEnums.TrainWhenLowResource)))
                        {
                            var reservedNext = DateTime.Now.AddSeconds(QueueDeferRules.NextBuildingSeconds(false, null, intervalSeconds, Random.Shared));
                            TrainTroopSchedule.Set(task.VillageId, building, reservedNext, plan.Troop);
                            nextTimes.Add(reservedNext);
                            continue;
                        }
                        reserveAmount = allowed;
                    }

                    result = await trainTroopCommand.HandleAsync(new(task.VillageId, building, plan, reserveAmount), cancellationToken);
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
                    if (queueSeconds is not null) VillageOverviewStore.SetTrainQueue(task.VillageId.Value, (int)building, queueSeconds.Value);
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
                "Train troop in {VillageId}: task next runs at {Time:HH:mm:ss} (the earliest of the buildings' own visit times).",
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
            long[]? reserveFloor,
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
            // 2026-11-02: the village could not pay what the horizon asked for (see QueueHorizonRules.PlanNextVisit).
            var starved = false;

            logger.Information(
                "Train troop in {VillageId}: {Building} queue horizon - queue holds {Queue} min, fill up to {FillTo} min (at least {Min} min, target {Target} min).",
                task.VillageId, building, queueSeconds / 60, fillTo / 60, horizon.MinSeconds / 60, horizon.TargetSeconds / 60);

            foreach (var plan in plans)
            {
                if (cancellationToken.IsCancellationRequested) return Cancel.Error;

                var need = QueueHorizonRules.NeedSeconds(fillTo, queueNow);
                if (need <= 0) break;

                // 2026-11-02: the troop's own "not again before" interval also holds inside the horizon flow, so
                // Phalanx and Swordsman are trained at different visits (a troop whose interval is 0/0 has no limit here).
                var troopNotBefore = TrainTroopSchedule.GetNotBefore(task.VillageId, building, plan.Troop);
                if (troopNotBefore > DateTime.Now)
                {
                    logger.Information("Train troop in {VillageId}: {Troop} is not due before {Time:HH:mm:ss}, skipped this visit.", task.VillageId, plan.Troop, troopNotBefore);
                    continue;
                }

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

                var (makeUpResult, navigated, allowedUnits) = await MakeUpResources(
                    task, plan, units, reserveFloor, context, browser, settingService, logger,
                    toNpcResourcePageCommand, npcExchangeToTargetCommand, useHeroResourceCommand, cancellationToken);
                if (makeUpResult.IsFailed) return makeUpResult;

                // The NPC page / hero inventory replaced the training page: go back before typing the amount.
                if (navigated)
                {
                    result = await toTrainTroopPageCommand.HandleAsync(new(task.VillageId, building), cancellationToken);
                    if (result.IsFailed) return result;
                }

                // 2026-11-07: the construction reserve (or what the hero can add on top of it) allows fewer units.
                if (allowedUnits < units)
                {
                    starved = true;
                    logger.Information(
                        "Train troop in {VillageId}: {Troop} limited to {Allowed} of {Units} unit(s) by the construction reserve.",
                        task.VillageId, plan.Troop, allowedUnits, units);
                    units = allowedUnits;
                    if (units <= 0) continue;
                }

                // What the page lets us train right now (TrainTroopCommand caps the amount to this): fewer than
                // wanted = the village cannot pay the rest; the queue estimate below uses the capped number.
                var pageMax = TrainTroopParser.GetMaxAmount(browser.Html, plan.Troop);
                var unitsToTrain = pageMax > 0 ? Math.Min(units, pageMax) : units;
                if (pageMax < units) starved = true;

                var trainResult = await trainTroopCommand.HandleAsync(new(task.VillageId, building, plan, units), cancellationToken);
                if (trainResult.IsFailed)
                {
                    if (trainResult.HasError<MissingResource>())
                    {
                        starved = true;
                        logger.Information("Train troop in {VillageId}: not enough resources for {Troop} in {Building} (wanted {Units}).", task.VillageId, plan.Troop, building, units);
                        continue;
                    }
                    return trainResult;
                }

                trainedAny = true;
                if (plan.RepeatMin > 0 || plan.RepeatMax > 0)
                {
                    var troopNext = DateTime.Now.AddSeconds(GetIntervalSeconds(settingService, task.VillageId, building, plan));
                    TrainTroopSchedule.Set(task.VillageId, building, troopNext, plan.Troop);
                }
                var after = await QueueWait.ReadSecondsAfterActionAsync(browser, cancellationToken);
                queueNow = after ?? queueNow + unitsToTrain * Math.Max(0, unitSeconds);
            }

            // Next visit: when the queue has "at least" minutes left. A village that could not pay is not visited
            // again before the starved retry time (5-10 min); a target missed for another reason (per-visit cap)
            // is looked at again soon. See QueueHorizonRules.PlanNextVisit.
            var visit = QueueHorizonRules.PlanNextVisit(
                DateTime.Now, fillTo, queueNow, horizon.MinSeconds, offlineStart, starved, QueueHorizonRules.StarvedRetrySeconds(Random.Shared));
            // Every troop still waits for its own interval: no point in coming back before the first one is due.
            var next = QueueHorizonRules.HoldUntilAnyTroopDue(
                visit.At, plans.Select(x => TrainTroopSchedule.GetNotBefore(task.VillageId, building, x.Troop)).ToList(), DateTime.Now);
            logger.Information(
                "Train troop in {VillageId}: {Building} next visit at {Time:HH:mm:ss} ({Reason}; queue holds {Queue} min).",
                task.VillageId, building, next, visit.Reason, queueNow / 60);

            // 2026-11-11 (village overview tab): the queue length as it is after this visit.
            VillageOverviewStore.SetTrainQueue(task.VillageId.Value, (int)building, queueNow);

            TrainTroopSchedule.Set(task.VillageId, building, next);
            nextTimes.Add(next);
            return Result.Ok();
        }

        // A batch that costs more than the village holds: first an NPC trade (when it can cover the deficit by
        // redistributing the village's own total), then the hero's inventory (above the reserve). Returns whether
        // the browser left the training page (the caller then opens it again) and how many units the caller may
        // train. A failure here is never fatal: the caller trains what the village can pay.
        //
        // 2026-11-07 (construction reserve): with a floor the village's own stock counts only ABOVE the floor. An NPC
        // trade would redistribute the very stock the floor protects, so it is skipped; the hero's inventory is still
        // used for the part above the spendable stock. The returned unit count is then what that adds up to (0 = the
        // reserve leaves nothing). Without a floor the count is always `units` (the old behaviour).
        private static async ValueTask<(Result Result, bool Navigated, int Units)> MakeUpResources(
            Task task,
            TroopPlan plan,
            int units,
            long[]? floor,
            AppDbContext context,
            IChromeBrowser browser,
            ISettingService settingService,
            ILogger logger,
            ToNpcResourcePageCommand.Handler toNpcResourcePageCommand,
            NpcExchangeToTargetCommand.Handler npcExchangeToTargetCommand,
            UseHeroResourceCommand.Handler useHeroResourceCommand,
            CancellationToken cancellationToken)
        {
            var useNpc = context.BooleanByName(task.VillageId, VillageSettingEnums.TrainUseNpc) && floor is null;
            var useHero = context.BooleanByName(task.VillageId, VillageSettingEnums.TrainUseHeroResource);
            if (floor is null && !useNpc && !useHero) return (Result.Ok(), false, units);

            var html = browser.Html;
            if (floor is null && TrainTroopParser.GetMaxAmount(html, plan.Troop) >= units) return (Result.Ok(), false, units);

            var (wood, clay, iron, crop) = TrainTroopParser.GetUnitCost(html, plan.Troop);
            long[] unitCost = [wood, clay, iron, crop];
            if (!TrainResourceRules.HasCost(unitCost))
            {
                logger.Warning("Train troop in {VillageId}: the unit cost of {Troop} could not be read, no resources are made up.", task.VillageId, plan.Troop);
                return (Result.Ok(), false, units);
            }

            var stock = ReadStock(html, context, task.VillageId);
            // What training may spend from the village: all of it, or only the part above the construction floor.
            var spendable = floor is null ? stock : ConstructionReserveRules.Spendable(stock, floor);
            var allowed = floor is null ? units : TrainResourceRules.UnitsThatFit(units, unitCost, spendable, new long[4]);
            var needed = TrainResourceRules.Needed(unitCost, units);

            // Enough in the village: the page's own limit is something else (population, queue) - nothing to trade.
            if (!TrainResourceRules.AnyMissing(TrainResourceRules.Missing(needed, spendable))) return (Result.Ok(), false, allowed);

            var navigated = false;

            if (useNpc)
            {
                var target = TrainResourceRules.NpcTarget(stock, needed);
                if (target is not null)
                {
                    navigated = true;
                    var pageResult = await toNpcResourcePageCommand.HandleAsync(new(task.VillageId), cancellationToken);
                    if (pageResult.HasError<Cancel>()) return (pageResult, true, units);
                    if (pageResult.IsSuccess)
                    {
                        var npcResult = await npcExchangeToTargetCommand.HandleAsync(new(task.VillageId, target), cancellationToken);
                        if (npcResult.HasError<Cancel>()) return (npcResult, true, units);
                        if (npcResult.IsSuccess)
                        {
                            logger.Information("Train troop in {VillageId}: NPC trade for {Units} x {Troop} done (wood/clay/iron/crop {Target}).", task.VillageId, units, plan.Troop, string.Join("/", target));
                            return (Result.Ok(), true, units);
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

                var fit = TrainResourceRules.UnitsThatFit(units, unitCost, spendable, available);
                var give = TrainResourceRules.Missing(TrainResourceRules.Needed(unitCost, fit), spendable);
                if (floor is not null) allowed = fit;
                if (fit > 0 && TrainResourceRules.AnyMissing(give))
                {
                    navigated = true;
                    var heroResult = await useHeroResourceCommand.HandleAsync(new(task.AccountId, task.VillageId, give), cancellationToken);
                    if (heroResult.HasError<Cancel>()) return (heroResult, true, units);
                    if (heroResult.IsSuccess)
                    {
                        logger.Information("Train troop in {VillageId}: took {Give} (wood/clay/iron/crop) from the hero's inventory for {Units} x {Troop}.", task.VillageId, string.Join("/", give), fit, plan.Troop);
                    }
                    else
                    {
                        logger.Warning("Train troop in {VillageId}: the hero's inventory could not be used ({Reason}).", task.VillageId, string.Join("; ", heroResult.Reasons.Select(x => x.Message)));
                        // The hero's share did not arrive: only the village's own spendable stock is certain.
                        if (floor is not null) allowed = TrainResourceRules.UnitsThatFit(units, unitCost, spendable, new long[4]);
                    }
                }
                else if (fit <= 0)
                {
                    logger.Information("Train troop in {VillageId}: the hero's inventory (above the reserve {Reserve}) cannot cover {Troop}.", task.VillageId, reserve, plan.Troop);
                }
            }

            return (Result.Ok(), navigated, allowed);
        }

        // 2026-11-07: the construction reserve floor (wood / clay / iron / crop) or null when the setting is off or the
        // storage capacity is not known yet. Read once per visit.
        private static long[]? GetReserveFloor(Task task, AppDbContext context, ISettingService settingService, ILogger logger)
        {
            var percent = settingService.ByName(task.VillageId, VillageSettingEnums.ConstructionReservePercent);
            if (percent <= 0) return null;

            var storage = context.Storages.FirstOrDefault(x => x.VillageId == task.VillageId.Value);
            if (storage is null || storage.Warehouse <= 0 || storage.Granary <= 0) return null;

            var cost = ConstructionNeed.GetCost(task.VillageId);
            var busyFor = ConstructionNeed.GetSlotBusyFor(task.VillageId);
            var floor = ConstructionReserveRules.Floor(storage.Warehouse, storage.Granary, percent, cost, busyFor);
            logger.Information(
                "Train troop in {VillageId}: construction reserve {Percent}% - training keeps wood/clay/iron/crop at or above {Floor} (next building cost {Cost}, build queue busy for {Busy} min).",
                task.VillageId, percent, string.Join("/", floor), cost is null ? "unknown" : string.Join("/", cost.Take(4)), busyFor is null ? 0 : (int)busyFor.Value.TotalMinutes);
            return floor;
        }

        // How many of `wanted` units the stock above the floor pays for (plain, non-horizon flow). Reads the unit
        // cost and the stock off the training page; an unreadable cost leaves the amount alone.
        private static int ReserveUnits(IChromeBrowser browser, AppDbContext context, VillageId villageId, TroopEnums troop, int wanted, long[] floor, ILogger logger)
        {
            var html = browser.Html;
            var (wood, clay, iron, crop) = TrainTroopParser.GetUnitCost(html, troop);
            long[] unitCost = [wood, clay, iron, crop];
            if (!TrainResourceRules.HasCost(unitCost))
            {
                logger.Warning("Train troop in {VillageId}: the unit cost of {Troop} could not be read, the construction reserve is not applied.", villageId, troop);
                return wanted;
            }

            var stock = ReadStock(html, context, villageId);
            var allowed = ConstructionReserveRules.UnitsAllowed(wanted, unitCost, stock, floor);
            if (allowed < wanted)
            {
                logger.Information(
                    "Train troop in {VillageId}: {Troop} limited to {Allowed} of {Wanted} unit(s) by the construction reserve (stock {Stock}).",
                    villageId, troop, allowed, wanted, string.Join("/", stock));
            }
            return allowed;
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
