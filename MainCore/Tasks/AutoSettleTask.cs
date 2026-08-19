using MainCore.Commands.Features.Settle;
using MainCore.Models;
using MainCore.Tasks.Base;

namespace MainCore.Tasks
{
    // Auto Settle (2026-08-12/13, see CLAUDE.md/PROJECT_CONTEXT.md §5k): trains settlers in
    // this village's Residence/Palace and, once 3 are ready AND the 750-each founding cost is
    // covered, founds a new village at a fixed target coordinate. Any resource shortfall
    // (for training OR for the founding cost) is published via the NeedExpansion* settings so
    // SupplyForSettleTask can pull it in from sibling villages on demand - this village never
    // computes or requests from a specific sibling itself.
    //
    // 2026-08-18 fix: "present settler" readiness is tracked via the bot-maintained
    // VillageSettingEnums.AutoSettleSettlersReady counter, NOT re-parsed from the page on
    // every run - TrainTroopParser.GetPresentAmount is an unverified HTML guess and was
    // found to misreport the count, which let this task jump straight to founding before any
    // settler was ever trained (train+settle share one AutoSettleEnable tick, so the user has
    // no separate way to gate one on the other), and separately kept it retrying "found
    // village" after a coordinate was already settled. The counter is only ever advanced by
    // this task's own confirmed actions (see HandleAsync), so it can't drift the same way.
    // On a successful founding, AutoSettleEnable is also switched off for this village so it
    // doesn't try to train 3 more settlers and found again at the same (now-occupied) target.
    [Handler]
    public static partial class AutoSettleTask
    {
        // Fixed by the game: each of wood/clay/iron/crop, regardless of tribe/server speed.
        public const long FoundingCostPerResource = 750;

        public sealed class Task : VillageTask
        {
            public Task(AccountId accountId, VillageId villageId) : base(accountId, villageId)
            {
            }

            protected override string TaskName => "Auto settle";

            public override bool CanStart(AppDbContext context) =>
                context.BooleanByName(VillageId, VillageSettingEnums.AutoSettleEnable);
        }

        private static async ValueTask<Result> HandleAsync(
            Task task,
            AppDbContext context,
            IChromeBrowser browser,
            ToSettlerTrainPageCommand.Handler toSettlerTrainPageCommand,
            ToExpansionPageCommand.Handler toExpansionPageCommand,
            TrainSettlerCommand.Handler trainSettlerCommand,
            FoundNewVillageCommand.Handler foundNewVillageCommand,
            ITaskManager taskManager,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            var tribe = (TribeEnums)context.ByName(task.AccountId, AccountSettingEnums.Tribe);
            var slots = RallyPointTroopSlots.GetSlots(tribe);
            if (slots.Count < 10)
            {
                logger.Warning("Cannot determine the settler troop for tribe {Tribe}.", tribe);
                return Skip.Error;
            }
            var settlerTroop = slots[9]; // slot 10 = settler, for every tribe (see RallyPointTroopSlots)

            var building = GetSettleBuilding(context, task.VillageId);
            if (building is null)
            {
                logger.Warning("No Residence/Palace in village {VillageId}, cannot auto settle.", task.VillageId);
                return Skip.Error;
            }

            var trainPageResult = await toSettlerTrainPageCommand.HandleAsync(new(task.VillageId, building.Value), cancellationToken);
            if (trainPageResult.IsFailed)
            {
                if (trainPageResult.HasError<MissingBuilding>()) return Skip.Error;
                return trainPageResult;
            }

            // Authoritative: our own persisted counter (see the type comment above), not a
            // re-parse of the page. GetUnitCost is still HTML-derived, but that's lower-risk -
            // it only affects how much resource we ask siblings for, never the "ready to
            // found" gate.
            var present = context.ByName(task.VillageId, VillageSettingEnums.AutoSettleSettlersReady);
            var (unitWood, unitClay, unitIron, unitCrop) = TrainTroopParser.GetUnitCost(browser.Html, settlerTroop);

            if (present < 3)
            {
                var maxNow = TrainTroopParser.GetMaxAmount(browser.Html, settlerTroop);
                var wanted = 3 - present;
                var trainNow = Math.Min(maxNow, wanted);

                if (trainNow > 0)
                {
                    var trainResult = await trainSettlerCommand.HandleAsync(new(task.VillageId, settlerTroop, trainNow), cancellationToken);
                    if (trainResult.IsFailed) return trainResult;
                    present += trainNow;
                    SetSetting(context, task.VillageId, VillageSettingEnums.AutoSettleSettlersReady, present);
                }

                var stillNeeded = 3 - present;
                if (stillNeeded > 0)
                {
                    UpdateNeedExpansion(context, task.AccountId, task.VillageId, taskManager, logger,
                        unitWood * stillNeeded, unitClay * stillNeeded, unitIron * stillNeeded, unitCrop * stillNeeded);
                    logger.Information("Village {VillageId} needs resources for {Count} more settler(s).", task.VillageId, stillNeeded);
                }
                else
                {
                    UpdateNeedExpansion(context, task.AccountId, task.VillageId, taskManager, logger, 0, 0, 0, 0);
                }

                return Result.Ok();
            }

            // 3 settlers confirmed ready - check the 750-each founding cost before spending the click.
            var storage = context.Storages.FirstOrDefault(x => x.VillageId == task.VillageId.Value);
            if (storage is null) return Skip.Error;

            var missingWood = Math.Max(0, FoundingCostPerResource - storage.Wood);
            var missingClay = Math.Max(0, FoundingCostPerResource - storage.Clay);
            var missingIron = Math.Max(0, FoundingCostPerResource - storage.Iron);
            var missingCrop = Math.Max(0, FoundingCostPerResource - storage.Crop);

            if (missingWood > 0 || missingClay > 0 || missingIron > 0 || missingCrop > 0)
            {
                UpdateNeedExpansion(context, task.AccountId, task.VillageId, taskManager, logger, missingWood, missingClay, missingIron, missingCrop);
                logger.Information("Village {VillageId} has 3 settlers ready but is short on founding resources.", task.VillageId);
                return Result.Ok();
            }

            UpdateNeedExpansion(context, task.AccountId, task.VillageId, taskManager, logger, 0, 0, 0, 0);

            var targetX = context.ByName(task.VillageId, VillageSettingEnums.AutoSettleTargetX);
            var targetY = context.ByName(task.VillageId, VillageSettingEnums.AutoSettleTargetY);

            var expansionPageResult = await toExpansionPageCommand.HandleAsync(new(task.VillageId, building.Value), cancellationToken);
            if (expansionPageResult.IsFailed)
            {
                if (expansionPageResult.HasError<MissingBuilding>()) return Skip.Error;
                return expansionPageResult;
            }

            var foundResult = await foundNewVillageCommand.HandleAsync(new(task.VillageId, targetX, targetY), cancellationToken);
            if (foundResult.IsFailed) return foundResult;

            // Founding confirmed - the 3 settlers are spent and this coordinate is now taken.
            // Reset the counter and stop Auto Settle for this village so it doesn't try to
            // train 3 more and found again at the same (now-occupied) target.
            SetSetting(context, task.VillageId, VillageSettingEnums.AutoSettleSettlersReady, 0);
            SetSetting(context, task.VillageId, VillageSettingEnums.AutoSettleEnable, 0);
            logger.Information("Village {VillageId} founded a new village at ({X}|{Y}) - Auto Settle turned off for this village.", task.VillageId, targetX, targetY);

            return Result.Ok();
        }

        private static BuildingEnums? GetSettleBuilding(AppDbContext context, VillageId villageId)
        {
            var hasResidence = context.Buildings.Any(x => x.VillageId == villageId.Value && x.Type == BuildingEnums.Residence);
            if (hasResidence) return BuildingEnums.Residence;

            var hasPalace = context.Buildings.Any(x => x.VillageId == villageId.Value && x.Type == BuildingEnums.Palace);
            if (hasPalace) return BuildingEnums.Palace;

            return null;
        }

        private static void UpdateNeedExpansion(
            AppDbContext context, AccountId accountId, VillageId villageId, ITaskManager taskManager, ILogger logger,
            long wood, long clay, long iron, long crop)
        {
            SetSetting(context, villageId, VillageSettingEnums.NeedExpansionWood, wood);
            SetSetting(context, villageId, VillageSettingEnums.NeedExpansionClay, clay);
            SetSetting(context, villageId, VillageSettingEnums.NeedExpansionIron, iron);
            SetSetting(context, villageId, VillageSettingEnums.NeedExpansionCrop, crop);

            SupplyForSettleTask.RequestIfNeeded(context, accountId, villageId, taskManager, logger);
        }

        private static void SetSetting(AppDbContext context, VillageId villageId, VillageSettingEnums setting, long value)
        {
            var clamped = (int)Math.Clamp(value, 0, int.MaxValue);
            context.VillagesSetting
                .Where(x => x.VillageId == villageId.Value && x.Setting == setting)
                .ExecuteUpdate(x => x.SetProperty(x => x.Value, clamped));
        }
    }
}
