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

            var toBuildingResult = await toBuildingByTypeCommand.HandleAsync(
                new(command.VillageId, BuildingEnums.Cranny), cancellationToken);
            if (toBuildingResult.IsFailed) return Result.Fail(toBuildingResult.Errors);

            var amount = CrannyParser.GetProtectedAmount(browser.Html);
            return amount;
        }
    }
}
