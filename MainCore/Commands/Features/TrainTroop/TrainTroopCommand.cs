#pragma warning disable S1172

namespace MainCore.Commands.Features.TrainTroop
{
    [Handler]
    public static partial class TrainTroopCommand
    {
        // 2026-10-27: trains ONE troop of the building (the caller loops over the troops that are due).
        // 2026-10-30: Amount > 0 = the caller (queue horizon) decided the number of units; it is capped to what the
        // page allows and never refused for "low resource" (the caller already made up what it could). 0 = the
        // old behaviour: a random amount from the plan's min-max.
        public sealed record Command(VillageId VillageId, BuildingEnums Building, TroopPlan Plan, int Amount = 0) : IVillageCommand;

        private static async ValueTask<Result> HandleAsync(
            Command command,
            AppDbContext context,
            IChromeBrowser browser,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            var (villageId, _, plan, explicitAmount) = command;
            var troop = plan.Troop;
            var maxAmount = TrainTroopParser.GetMaxAmount(browser.Html, troop);
            if (maxAmount == 0)
            {
                return MissingResource.Error(troop);
            }

            var amount = explicitAmount > 0 ? Math.Min(explicitAmount, maxAmount) : TrainTroopPlanner.PickAmount(plan, Random.Shared);
            if (explicitAmount <= 0 && amount > maxAmount)
            {
                var trainWhenLowResource = context.BooleanByName(villageId, VillageSettingEnums.TrainWhenLowResource);
                if (!trainWhenLowResource)
                {
                    return MissingResource.Error(troop);
                }

                // "Train maximum number if low on resource": train what the resources allow.
                amount = maxAmount;
            }

            var result = await TrainTroop(browser, troop, amount, cancellationToken);
            if (result.IsFailed) return result;

            logger.Information("Troop training for {Troop} with amount {Amount} is done.", troop, amount);
            return Result.Ok();
        }

        public static Dictionary<BuildingEnums, VillageSettingEnums> TroopSettings { get; } = new()
        {
            {BuildingEnums.Barracks, VillageSettingEnums.BarrackTroop },
            {BuildingEnums.Stable, VillageSettingEnums.StableTroop },
            {BuildingEnums.GreatBarracks, VillageSettingEnums.GreatBarrackTroop },
            {BuildingEnums.GreatStable, VillageSettingEnums.GreatStableTroop },
            {BuildingEnums.Workshop, VillageSettingEnums.WorkshopTroop },
        };

        public static Dictionary<BuildingEnums, (VillageSettingEnums, VillageSettingEnums)> AmountSettings { get; } = new()
        {
            {BuildingEnums.Barracks, (VillageSettingEnums.BarrackAmountMin,VillageSettingEnums.BarrackAmountMax ) },
            {BuildingEnums.Stable, (VillageSettingEnums.StableAmountMin,VillageSettingEnums.StableAmountMax ) },
            {BuildingEnums.GreatBarracks, (VillageSettingEnums.GreatBarrackAmountMin,VillageSettingEnums.GreatBarrackAmountMax ) },
            {BuildingEnums.GreatStable, (VillageSettingEnums.GreatStableAmountMin,VillageSettingEnums.GreatStableAmountMax ) },
            {BuildingEnums.Workshop, (VillageSettingEnums.WorkshopAmountMin,VillageSettingEnums.WorkshopAmountMax ) },
        };

        // 2026-10-26: per-building "not again before" interval in minutes (0/0 = use TrainTroopRepeatTimeMin/Max).
        public static Dictionary<BuildingEnums, (VillageSettingEnums, VillageSettingEnums)> RepeatSettings { get; } = new()
        {
            {BuildingEnums.Barracks, (VillageSettingEnums.BarrackRepeatTimeMin,VillageSettingEnums.BarrackRepeatTimeMax ) },
            {BuildingEnums.Stable, (VillageSettingEnums.StableRepeatTimeMin,VillageSettingEnums.StableRepeatTimeMax ) },
            {BuildingEnums.GreatBarracks, (VillageSettingEnums.GreatBarrackRepeatTimeMin,VillageSettingEnums.GreatBarrackRepeatTimeMax ) },
            {BuildingEnums.GreatStable, (VillageSettingEnums.GreatStableRepeatTimeMin,VillageSettingEnums.GreatStableRepeatTimeMax ) },
            {BuildingEnums.Workshop, (VillageSettingEnums.WorkshopRepeatTimeMin,VillageSettingEnums.WorkshopRepeatTimeMax ) },
        };

        // 2026-10-30: per-building queue horizon in minutes: (at least, fill up to); 0/0 = off for that building.
        public static Dictionary<BuildingEnums, (VillageSettingEnums, VillageSettingEnums)> HorizonSettings { get; } = new()
        {
            {BuildingEnums.Barracks, (VillageSettingEnums.BarrackHorizonMin,VillageSettingEnums.BarrackHorizonTarget ) },
            {BuildingEnums.Stable, (VillageSettingEnums.StableHorizonMin,VillageSettingEnums.StableHorizonTarget ) },
            {BuildingEnums.GreatBarracks, (VillageSettingEnums.GreatBarrackHorizonMin,VillageSettingEnums.GreatBarrackHorizonTarget ) },
            {BuildingEnums.GreatStable, (VillageSettingEnums.GreatStableHorizonMin,VillageSettingEnums.GreatStableHorizonTarget ) },
            {BuildingEnums.Workshop, (VillageSettingEnums.WorkshopHorizonMin,VillageSettingEnums.WorkshopHorizonTarget ) },
        };

        private static async ValueTask<Result> TrainTroop(
            IChromeBrowser browser,
            TroopEnums troop,
            long amount,
            CancellationToken cancellationToken)
        {
            var (_, isFailed, element, errors) = await browser.GetElement(doc => TrainTroopParser.GetInputBox(doc, troop), cancellationToken);
            if (isFailed) return Result.Fail(errors);

            Result result;
            result = await browser.Input(element, $"{amount}", cancellationToken);
            if (result.IsFailed) return result;

            (_, isFailed, element, errors) = await browser.GetElement(doc => TrainTroopParser.GetTrainButton(doc), cancellationToken);
            if (isFailed) return Result.Fail(errors);

            result = await browser.Click(element, cancellationToken);
            if (result.IsFailed) return result;

            return Result.Ok();
        }
    }
}