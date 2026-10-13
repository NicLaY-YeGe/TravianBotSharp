namespace MainCore.Commands.Navigate
{
    [Handler]
    public static partial class ToBuildingByTypeCommand
    {
        public sealed record Command(VillageId VillageId, BuildingEnums Type) : IVillageCommand;

        private static async ValueTask<Result> HandleAsync(
           Command command,
           IChromeBrowser browser,
           AppDbContext context,
           CancellationToken cancellationToken
           )
        {
            // Only a building that is actually BUILT (Level > 0) has a usable page. A level-0 plot
            // of this type (e.g. a Marketplace that is only queued/under construction - live case
            // TS-3204, 2026-10-12) opens the "construct" screen with no feature tabs, so navigating
            // on and looking for them ended in "Found 0 tabs" retries that paused the whole bot.
            // Treat it as missing instead; callers already handle MissingBuilding gracefully.
            var marketLocation = context.Buildings
                .Where(x => x.VillageId == command.VillageId.Value)
                .Where(x => x.Type == command.Type)
                .Where(x => x.Level > 0)
                .OrderByDescending(x => x.Level)
                .Select(x => x.Location)
                .FirstOrDefault();

            if (marketLocation == default)
            {
                return MissingBuilding.Error(command.Type);
            }

            return await ToBuildingByLocationCommand.ToBuilding(marketLocation, browser, cancellationToken);
        }
    }
}