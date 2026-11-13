using MainCore.Commands.Features.Scheduling;
using MainCore.Commands.Features.ShareTrain;
using MainCore.Commands.Features.TrainTroop;
using MainCore.Commands.Features.VillageOverview;
using MainCore.Commands.UI.Misc;
using MainCore.Tasks.Base;

namespace MainCore.Tasks
{
    // 2026-11-13, user decision: SHARE-BASED troop training - a mode of its own, kept apart from TrainTroopTask so the
    // old mode can be switched back to at any time (village setting TrainShareEnable; the two tasks exclude each other
    // through their CanStart).
    //
    // No visit intervals and no unit amounts. Whenever the stock above the construction reserve floor is worth at least
    // TrainShareTriggerPercent of the storage capacity (CanStart, checked after every storage read) the whole surplus is
    // split between the troops that have a weight (TrainShare<...> settings, 0 = not trained) in proportion to the
    // weights; whatever a troop cannot use flows on to the others (ShareTrainRules.Allocate). One visit = a SURVEY pass
    // (open each weighted building once: unit cost, unit time, queue length, is the troop trainable) and a TRAINING pass
    // (open each building that got units, train, read the queue). Buildings' queues are never filled beyond
    // TrainShareQueueMaxMinutes. No NPC trade and no hero inventory: only the village's own stock above the floor.
    // Great Barracks / Great Stable are not part of this mode.
    //
    // Lifecycle (like BuyCropTask, not bootstrapped in RxQueue on purpose): UpdateStorageCommand and
    // SaveVillageSettingCommand queue it whenever CanStart passes; it ends with ExecuteAt unchanged so TimerManager removes
    // it (CLAUDE.md rule 16 - intended); the "not again before" time lives in ShareTrainMemory.
    //
    // Secondary task (CLAUDE.md rule 4): hard time budget, no failure is ever Retry or Stop. A user pause stays a pause, a
    // dead browser is the behaviors' business, everything else becomes "look again later".
    [Handler]
    public static partial class ShareTrainTask
    {
        public sealed class Task : VillageTask
        {
            public Task(AccountId accountId, VillageId villageId) : base(accountId, villageId)
            {
            }

            protected override string TaskName => "Share train";

            public override bool CanStart(AppDbContext context)
            {
                if (!context.BooleanByName(VillageId, VillageSettingEnums.TrainShareEnable)) return false;
                if (!ShareTrainMemory.IsDue(VillageId.Value)) return false;
                if (!ShareTrainSlots.AllKeys().Any(key => context.ByName(VillageId, key) > 0)) return false;

                var surplus = ReadDbSurplus(context, VillageId, out var warehouse, out var granary, out _);
                if (surplus is null) return false;
                return ShareTrainRules.ShouldRun(surplus, warehouse, granary, context.ByName(VillageId, VillageSettingEnums.TrainShareTriggerPercent));
            }
        }

        private const int BudgetSeconds = 300;

        private static async ValueTask<Result> HandleAsync(
            Task task,
            AppDbContext context,
            IChromeBrowser browser,
            ToTrainTroopPageCommand.Handler toTrainTroopPageCommand,
            TrainTroopCommand.Handler trainTroopCommand,
            SaveVillageSettingCommand.Handler saveVillageSettingCommand,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(BudgetSeconds));

            var (result, trainedAny) = await RunAsync(task, context, browser, toTrainTroopPageCommand, trainTroopCommand, saveVillageSettingCommand, logger, deadline.Token);
            if (result.IsSuccess)
            {
                ShareTrainMemory.SetNextCheck(task.VillageId.Value, ShareTrainRules.NextCheck(DateTime.Now, trainedAny, Random.Shared));
                return result;
            }

            // A user pause stays a pause; a dead browser is the behaviors' business.
            if (cancellationToken.IsCancellationRequested) return result;
            if (result.HasError<BrowserClosed>()) return result;

            var timedOut = deadline.IsCancellationRequested;
            if (timedOut) await PageDump.SaveAsync(browser, logger, "sharetrain-timeout");

            var reasons = string.Join(" | ", result.Reasons.Select(x => x.Message).Where(x => !string.IsNullOrWhiteSpace(x)));
            if (timedOut) reasons = $"no answer within {BudgetSeconds}s" + (reasons.Length > 0 ? $" ({reasons})" : "");

            var next = ShareTrainRules.NextCheck(DateTime.Now, false, Random.Shared);
            ShareTrainMemory.SetNextCheck(task.VillageId.Value, next);
            logger.Warning("Share train in {VillageId}: could not finish ({Reasons}) - next look at {Next:HH:mm:ss}.", task.VillageId, reasons, next);
            return Result.Ok();
        }

        private static Result Cancelled => Cancel.Error;

        private sealed record Surveyed(ShareWeight Weight, int Building, long[] UnitCost, int UnitSeconds);

        // The token carries the user's pause plus the time budget (every browser call gets it).
        private static async ValueTask<(Result Result, bool TrainedAny)> RunAsync(
            Task task,
            AppDbContext context,
            IChromeBrowser browser,
            ToTrainTroopPageCommand.Handler toTrainTroopPageCommand,
            TrainTroopCommand.Handler trainTroopCommand,
            SaveVillageSettingCommand.Handler saveVillageSettingCommand,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            var villageId = task.VillageId;

            // CanStart is only checked when a task is queued: a task that waited while the user switched back to the old
            // mode ends here without doing anything.
            if (!context.BooleanByName(villageId, VillageSettingEnums.TrainShareEnable)) return (Result.Ok(), false);

            // Re-decide against the live numbers: the task may have waited in the queue while the situation changed.
            var dbSurplus = ReadDbSurplus(context, villageId, out var warehouse, out var granary, out var floor);
            var reservePercent = context.ByName(villageId, VillageSettingEnums.ConstructionReservePercent);
            if (reservePercent <= 0)
            {
                logger.Warning("Share train in {VillageId}: the construction reserve is 0 - without a reserve this mode would spend everything, so nothing is trained. Set a reserve above 0.", villageId);
                return (Result.Ok(), false);
            }
            var trigger = context.ByName(villageId, VillageSettingEnums.TrainShareTriggerPercent);
            if (dbSurplus is null || floor is null || !ShareTrainRules.ShouldRun(dbSurplus, warehouse, granary, trigger))
            {
                logger.Information("Share train in {VillageId}: the stock above the reserve is below the trigger ({Trigger}% of the storage) - nothing to do.", villageId, trigger);
                return (Result.Ok(), false);
            }

            var tribe = (TribeEnums)context.ByName(villageId, VillageSettingEnums.Tribe);
            var weights = ShareTrainSlots.GetWeights(tribe, key => context.ByName(villageId, key));
            if (weights.Count == 0)
            {
                logger.Information("Share train in {VillageId}: no troop has a weight above 0 for this tribe - nothing to do.", villageId);
                return (Result.Ok(), false);
            }

            var queueMax = context.ByName(villageId, VillageSettingEnums.TrainShareQueueMaxMinutes);

            // ---- Pass 1: survey. One visit per weighted building.
            var surveyed = new List<Surveyed>();
            var budgets = new Dictionary<int, long>();
            var queueNow = new Dictionary<int, int>();
            long[]? liveStock = null;
            foreach (var group in weights.GroupBy(x => x.Building))
            {
                if (cancellationToken.IsCancellationRequested) return (Cancelled, false);

                var building = group.Key;
                var pageResult = await toTrainTroopPageCommand.HandleAsync(new(villageId, building), cancellationToken);
                if (pageResult.IsFailed)
                {
                    if (pageResult.HasError<MissingBuilding>())
                    {
                        logger.Warning("Share train in {VillageId}: no {Building} in this village - its weights are set to 0.", villageId, building);
                        await ZeroBuildingAsync(task, building, saveVillageSettingCommand, cancellationToken);
                        continue;
                    }
                    return (pageResult, false);
                }

                var html = browser.Html;
                liveStock = ReadStock(html, context, villageId);
                var queueSeconds = TrainQueueParser.GetSecondsRemaining(html) ?? 0;
                queueNow[(int)building] = queueSeconds;
                budgets[(int)building] = ShareTrainRules.GroupBudgetSeconds(queueMax, queueSeconds);
                VillageOverviewStore.SetTrainQueue(villageId.Value, (int)building, queueSeconds);

                foreach (var weight in group)
                {
                    if (TrainTroopParser.GetInputBox(html, weight.Troop) is null)
                    {
                        logger.Information("Share train in {VillageId}: {Troop} cannot be trained in the {Building} right now (not researched?) - skipped.", villageId, weight.Troop, building);
                        continue;
                    }

                    var (wood, clay, iron, crop) = TrainTroopParser.GetUnitCost(html, weight.Troop);
                    long[] unitCost = [wood, clay, iron, crop];
                    if (!ShareTrainRules.HasCost(unitCost))
                    {
                        logger.Warning("Share train in {VillageId}: the unit cost of {Troop} could not be read - skipped.", villageId, weight.Troop);
                        continue;
                    }

                    var unitSeconds = TrainTroopParser.GetUnitSeconds(html, weight.Troop);
                    if (unitSeconds <= 0 && queueMax > 0)
                    {
                        logger.Warning("Share train in {VillageId}: the training time of {Troop} could not be read - the queue ceiling cannot be applied to it.", villageId, weight.Troop);
                    }
                    surveyed.Add(new Surveyed(weight, (int)building, unitCost, unitSeconds));
                }
            }

            if (surveyed.Count == 0 || liveStock is null)
            {
                logger.Information("Share train in {VillageId}: no weighted troop can be trained right now - nothing to do.", villageId);
                return (Result.Ok(), false);
            }

            // The pool is what the pages showed just now, above the floor.
            var pool = ShareTrainRules.Surplus(liveStock, floor);
            var candidates = surveyed
                .Select((x, i) => new ShareCandidate(i, x.Building, x.Weight.Weight, x.UnitCost, x.UnitSeconds))
                .ToList();
            var units = ShareTrainRules.Allocate(candidates, budgets, pool);

            logger.Information(
                "Share train in {VillageId}: stock above the reserve {Pool} (wood/clay/iron/crop), plan {Plan}.",
                villageId,
                string.Join("/", pool),
                string.Join(", ", surveyed.Select((x, i) => $"{x.Weight.Troop} w{x.Weight.Weight} -> {units[i]}")));

            // ---- Pass 2: training. One visit per building that got units.
            var trainedAny = false;
            foreach (var group in surveyed.Select((x, i) => (Item: x, Index: i)).Where(x => units[x.Index] > 0).GroupBy(x => x.Item.Building))
            {
                if (cancellationToken.IsCancellationRequested) return (Cancelled, trainedAny);

                var building = (BuildingEnums)group.Key;
                var trainedHere = false;
                foreach (var (item, index) in group)
                {
                    if (cancellationToken.IsCancellationRequested) return (Cancelled, trainedAny);

                    // Every batch starts from a clean training form (after a batch the old page is stale).
                    var pageResult = await toTrainTroopPageCommand.HandleAsync(new(villageId, building), cancellationToken);
                    if (pageResult.IsFailed) return (pageResult, trainedAny);

                    var plan = new TroopPlan(item.Weight.Slot, item.Weight.Troop, 0, 0, 0, 0);
                    var trainResult = await trainTroopCommand.HandleAsync(new(villageId, building, plan, units[index]), cancellationToken);
                    if (trainResult.IsFailed)
                    {
                        if (trainResult.HasError<MissingResource>())
                        {
                            logger.Information("Share train in {VillageId}: not enough resources for {Troop} in {Building} (planned {Units}).", villageId, item.Weight.Troop, building, units[index]);
                            continue;
                        }
                        return (trainResult, trainedAny);
                    }

                    trainedAny = true;
                    trainedHere = true;
                }

                if (trainedHere)
                {
                    var after = await QueueWait.ReadSecondsAfterActionAsync(browser, cancellationToken);
                    if (after is not null) VillageOverviewStore.SetTrainQueue(villageId.Value, (int)building, after.Value);
                }
            }

            return (Result.Ok(), trainedAny);
        }

        // The stock above the floor from the DATABASE (what CanStart and the re-check use): null when the storage is not
        // known yet or the reserve is off. `floor` is the construction floor (wood / clay / iron / crop).
        private static long[]? ReadDbSurplus(AppDbContext context, VillageId villageId, out long warehouse, out long granary, out long[]? floor)
        {
            warehouse = 0;
            granary = 0;
            floor = null;

            var percent = context.ByName(villageId, VillageSettingEnums.ConstructionReservePercent);
            if (percent <= 0) return null;

            var storage = context.Storages.FirstOrDefault(x => x.VillageId == villageId.Value);
            if (storage is null || storage.Warehouse <= 0 || storage.Granary <= 0) return null;

            warehouse = storage.Warehouse;
            granary = storage.Granary;
            floor = ConstructionReserveRules.Floor(
                storage.Warehouse, storage.Granary, percent,
                ConstructionNeed.GetCost(villageId), ConstructionNeed.GetSlotBusyFor(villageId));
            long[] stock = [storage.Wood, storage.Clay, storage.Iron, storage.Crop];
            return ShareTrainRules.Surplus(stock, floor);
        }

        // Wood / Clay / Iron / Crop off the page header; a value the page could not give (the wood node reads -1 on some
        // training pages, TODO P1) falls back to the stored storage.
        private static long[] ReadStock(HtmlDocument html, AppDbContext context, VillageId villageId)
        {
            long[] stock = [StorageParser.GetWood(html), StorageParser.GetClay(html), StorageParser.GetIron(html), StorageParser.GetCrop(html)];
            if (stock.All(x => x >= 0)) return stock;

            var storage = context.Storages.FirstOrDefault(x => x.VillageId == villageId.Value);
            long[] stored = storage is null ? [0, 0, 0, 0] : [storage.Wood, storage.Clay, storage.Iron, storage.Crop];
            for (var i = 0; i < 4; i++)
            {
                if (stock[i] < 0) stock[i] = stored[i];
            }
            return stock;
        }

        // MissingBuilding: switch the building's weights off.
        private static async ValueTask ZeroBuildingAsync(Task task, BuildingEnums building, SaveVillageSettingCommand.Handler saveVillageSettingCommand, CancellationToken cancellationToken)
        {
            var settings = new Dictionary<VillageSettingEnums, int>();
            for (var slot = 1; slot <= TrainTroopSlots.SlotCount(building); slot++)
            {
                settings[ShareTrainSlots.Key(building, slot)] = 0;
            }
            await saveVillageSettingCommand.HandleAsync(new(task.AccountId, task.VillageId, settings), cancellationToken);
        }
    }
}
