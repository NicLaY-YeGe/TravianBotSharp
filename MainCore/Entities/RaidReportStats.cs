namespace MainCore.Entities
{
    // What RaidReportTask has learned about ONE Raid List row from the offensive reports of its
    // target (stored as JSON in RaidListEntry.ReportStatsJson - a plain record so it round-trips
    // through System.Text.Json the same way TroopAmountRange does).
    //
    //   LastReportId     - highest report id already applied to this row (guards against
    //                      applying the same report twice)
    //   LastOutcome      - the report list's result icon: 1 = won without losses,
    //                      2 = won with losses, 3 = lost as attacker, 0 = nothing read yet
    //   LastLootPercent  - loot carried / carry capacity of the last report, 0-100;
    //                      -1 = unknown (nothing could be carried, or the report had no loot info)
    //   ConsecutiveLossy - reports in a row that were NOT "won without losses"
    //   LowLootStreak    - reports in a row that carried less than
    //                      RaidReportRules.LowLootThresholdPercent of capacity
    //   FullLootStreak   - reports in a row that filled the carry capacity completely
    //                      (troop amount too small for this target)
    //   ReportCount      - how many reports were applied in total
    public record RaidReportStats(
        long LastReportId,
        int LastOutcome,
        int LastLootPercent,
        int ConsecutiveLossy,
        int LowLootStreak,
        int FullLootStreak,
        int ReportCount)
    {
        public static readonly RaidReportStats Empty = new(0, 0, -1, 0, 0, 0, 0);
    }
}
