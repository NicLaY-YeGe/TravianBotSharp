namespace MainCore.Commands.Features.DefendDonate
{
    // Visits ONE Cranny building's own page and reads the protected-per-resource amount.
    // Per explicit user instruction, this does NOT visit every Cranny building in the village
    // and sum them - the game's own page already shows the village-wide total (CrannyParser
    // prefers the `overall` row when multiple crannies exist, falling back to `currentLevel`
    // for a single one), so any one Cranny building's page is enough.
    [Handler]
    public static partial class GetCrannyProtectionCommand
    {
        public sealed record Command(VillageId VillageId) : IVillageCommand;

        private static async ValueTask<Result<long>> HandleAsync(
           Command command,
           IChromeBrowser browser,
           AppDbContext context,
           ToDorfCommand.Handler toDorfCommand,
           ToBuildingByTypeCommand.Handler toBuildingByTypeCommand,
           CancellationToken cancellationToken
           )
        {
            var hasCranny = context.Buildings
                .Any(x => x.VillageId == command.VillageId.Value && x.Type == BuildingEnums.Cranny);

            if (!hasCranny)
            {
                // No Cranny at all in this village - nothing is protected, the whole stock
                // counts as "excess" for DefendDonateTask's purposes.
                return 0L;
            }

            // 2026-10-19: ToBuildingByTypeCommand clicks the building slot on dorf1/dorf2. The caller
            // leaves us on the Rally Point page (build.php), where no slot exists, so the lookup
            // waited the full 180 s ("Failed to find [building at #22]") and froze the whole
            // queue - the Dodge task behind it then ran after the attack had landed.
            var toDorfResult = await toDorfCommand.HandleAsync(new(2), cancellationToken);
            if (toDorfResult.IsFailed) return Result.Fail(toDorfResult.Errors);

            var toBuildingResult = await toBuildingByTypeCommand.HandleAsync(
                new(command.VillageId, BuildingEnums.Cranny), cancellationToken);
            if (toBuildingResult.IsFailed) return Result.Fail(toBuildingResult.Errors);

            var amount = CrannyParser.GetProtectedAmount(browser.Html);
            return amount;
        }
    }
}
