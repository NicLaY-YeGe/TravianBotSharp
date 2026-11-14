namespace MainCore.UI.Models.Output
{
    // 2026-11-11 (village overview tab): one table row, every cell already formatted (VillageOverviewRules). A record, so two
    // loads that produce the same text compare equal and the grid is not rebuilt for nothing.
    public sealed record VillageOverviewRow(
        string Name,
        string Wood,
        string Clay,
        string Iron,
        string Crop,
        string CropBalance,
        string Construction,
        string Troops,
        string Reinforcements,
        string Outside,
        string TroopsRead,
        string Merchants,
        string Barracks,
        string Stable,
        string Workshop,
        string Smithy,
        string Celebration);
}
