namespace MainCore.Commands.Features.DefendDonate
{
    // Trains as many of the village's tribe's T1 infantry unit (the cheapest troop every
    // tribe has - Legionnaire/Clubswinger/Phalanx/Slave Militia/Mercenary) as fit inside a
    // caller-supplied resource budget (Wood, Clay, Iron, Crop), via the Barracks. Added
    // 2026-09-27 for DefendDonateTask's "spend the excess-over-Cranny stock on troops before
    // an incoming attack lands" behaviour (explicit user request: always the smallest/cheapest
    // unit, always Barracks).
    //
    // NOTE: per-unit resource cost comes from TrainTroopParser.GetUnitCost, which carries the
    // same "not yet page-verified" caveat noted on that method itself - re-check if a real
    // Barracks page ever shows a different resourceWrapper/resource layout than assumed there.
    [Handler]
    public static partial class TrainCheapestTroopCommand
    {
        public sealed record Command(VillageId VillageId, long Wood, long Clay, long Iron, long Crop) : IVillageCommand;

        // Returns how many were actually queued (0 if the budget couldn't afford even one).
        private static async ValueTask<Result<int>> HandleAsync(
            Command command,
            AppDbContext context,
            IChromeBrowser browser,
            ToBuildingByTypeCommand.Handler toBuildingByTypeCommand,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            var hasBarracks = context.Buildings
                .Any(x => x.VillageId == command.VillageId.Value && x.Type == BuildingEnums.Barracks);
            if (!hasBarracks)
            {
                logger.Information("No Barracks in {VillageId}, skipping defensive troop training.", command.VillageId);
                return 0;
            }

            var tribe = context.AccountsInfo
                .Where(x => x.AccountId == context.Villages.First(v => v.Id == command.VillageId.Value).AccountId)
                .Select(x => x.Tribe)
                .FirstOrDefault();

            var troop = GetT1Troop(tribe);
            if (troop is null)
            {
                logger.Warning("Cannot determine T1 troop for tribe {Tribe} in {VillageId}.", tribe, command.VillageId);
                return 0;
            }

            var toBuildingResult = await toBuildingByTypeCommand.HandleAsync(
                new(command.VillageId, BuildingEnums.Barracks), cancellationToken);
            if (toBuildingResult.IsFailed) return Result.Fail(toBuildingResult.Errors);

            var (costWood, costClay, costIron, costCrop) = TrainTroopParser.GetUnitCost(browser.Html, troop.Value);
            var affordable = MaxAffordable(command.Wood, command.Clay, command.Iron, command.Crop,
                costWood, costClay, costIron, costCrop);

            if (affordable <= 0)
            {
                logger.Information("Not enough excess resources in {VillageId} to train even one {Troop}.", command.VillageId, troop.Value);
                return 0;
            }

            var gameMax = TrainTroopParser.GetMaxAmount(browser.Html, troop.Value);
            var amount = gameMax > 0 ? Math.Min(affordable, gameMax) : affordable;
            if (amount <= 0)
            {
                logger.Information("Barracks reports 0 trainable {Troop} in {VillageId} right now.", troop.Value, command.VillageId);
                return 0;
            }

            var (_, isFailed, element, errors) = await browser.GetElement(doc => TrainTroopParser.GetInputBox(doc, troop.Value), cancellationToken);
            if (isFailed) return Result.Fail(errors);

            Result result;
            result = await browser.Input(element, $"{amount}", cancellationToken);
            if (result.IsFailed) return Result.Fail(result.Errors);

            (_, isFailed, element, errors) = await browser.GetElement(doc => TrainTroopParser.GetTrainButton(doc), cancellationToken);
            if (isFailed) return Result.Fail(errors);

            result = await browser.Click(element, cancellationToken);
            if (result.IsFailed) return Result.Fail(result.Errors);

            logger.Information("Trained {Amount}x {Troop} in {VillageId} before incoming attack.", amount, troop.Value, command.VillageId);
            return (int)amount;
        }

        private static long MaxAffordable(
            long budgetWood, long budgetClay, long budgetIron, long budgetCrop,
            long costWood, long costClay, long costIron, long costCrop)
        {
            long? max = null;

            void Consider(long budget, long cost)
            {
                if (cost <= 0) return;
                var affordableForThisResource = budget / cost;
                if (max is null || affordableForThisResource < max) max = affordableForThisResource;
            }

            Consider(budgetWood, costWood);
            Consider(budgetClay, costClay);
            Consider(budgetIron, costIron);
            Consider(budgetCrop, costCrop);

            // If every cost came back 0 (parser miss), refuse to train an "unlimited" amount.
            return max ?? 0;
        }

        private static TroopEnums? GetT1Troop(TribeEnums tribe) => tribe switch
        {
            TribeEnums.Romans => TroopEnums.Legionnaire,
            TribeEnums.Teutons => TroopEnums.Clubswinger,
            TribeEnums.Gauls => TroopEnums.Phalanx,
            TribeEnums.Egyptians => TroopEnums.SlaveMilitia,
            TribeEnums.Huns => TroopEnums.Mercenary,
            _ => null,
        };
    }
}
