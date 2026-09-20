namespace MainCore.Commands.Features.RaidReport
{
    // The decision logic of RaidReportTask, kept free of any DB/browser access (same idea as
    // ResolveMissingPrerequisiteCommand.GetPrerequisitePlan) so every rule can be unit tested
    // against plain values.
    public static class RaidReportRules
    {
        // A raid that carried less than this share of its capacity counts as "low loot" (only
        // recorded and shown - it never pauses a row by itself: the very first real capture
        // had 5 of 8 raids below 30% and pausing all of those would have emptied the list).
        public const int LowLootThresholdPercent = 30;

        // "Won with losses" this many times in a row pauses the row. A single such report does
        // not: a lone defender or a crocodile-strength oasis can cost a unit or two once.
        public const int LossyPauseAfter = 2;

        // 0-100, rounded; -1 = unknown (nothing could be carried, e.g. a hero-only raid).
        public static int LootPercent(int carried, int capacity)
        {
            if (capacity <= 0) return -1;

            var percent = (int)Math.Round(100.0 * carried / capacity, MidpointRounding.AwayFromZero);
            return Math.Clamp(percent, 0, 100);
        }

        // Folds one report into a row's statistics. Reports already applied (id not newer than
        // LastReportId) leave the statistics untouched.
        public static RaidReportStats Apply(RaidReportStats previous, long reportId, int outcome, int lootPercent)
        {
            if (reportId <= previous.LastReportId) return previous;

            var consecutiveLossy = outcome switch
            {
                1 => 0,
                2 or 3 => previous.ConsecutiveLossy + 1,
                _ => previous.ConsecutiveLossy,
            };

            var lowLoot = previous.LowLootStreak;
            var fullLoot = previous.FullLootStreak;
            if (lootPercent >= 0)
            {
                lowLoot = lootPercent < LowLootThresholdPercent ? lowLoot + 1 : 0;
                fullLoot = lootPercent >= 100 ? fullLoot + 1 : 0;
            }

            return new RaidReportStats(reportId, outcome, lootPercent, consecutiveLossy, lowLoot, fullLoot, previous.ReportCount + 1);
        }

        // Why this row should be paused after the latest report, or null to leave it running.
        public static string? GetPauseReason(RaidReportStats stats)
        {
            if (stats.LastOutcome == 3)
            {
                return "the last raid was lost as attacker";
            }

            if (stats.LastOutcome == 2 && stats.ConsecutiveLossy >= LossyPauseAfter)
            {
                return $"the last {stats.ConsecutiveLossy} raids all cost troops";
            }

            return null;
        }

        // Short text for the Raid List row, e.g. "94% (full x2)" or "19% (low x3)"; empty until
        // a report has been read.
        public static string Summarize(RaidReportStats stats)
        {
            if (stats.ReportCount == 0) return "";

            var basis = stats.LastOutcome switch
            {
                3 => "LOST",
                2 => "losses",
                _ => stats.LastLootPercent >= 0 ? $"{stats.LastLootPercent}%" : "no loot",
            };

            var notes = new List<string>();
            if (stats.FullLootStreak >= 2) notes.Add($"full x{stats.FullLootStreak}");
            if (stats.LowLootStreak >= 2) notes.Add($"low x{stats.LowLootStreak}");
            if (stats.LastOutcome != 1 && stats.ConsecutiveLossy >= 2) notes.Add($"lossy x{stats.ConsecutiveLossy}");

            return notes.Count == 0 ? basis : $"{basis} ({string.Join(", ", notes)})";
        }

        // Rows of the Raid List whose target is (x|y). With a known source village only that
        // village's rows match; without one every village's rows do.
        public static List<RaidListEntry> MatchEntries(IEnumerable<RaidListEntry> entries, int targetX, int targetY, int? sourceVillageId)
        {
            return entries
                .Where(e => e.TargetX == targetX && e.TargetY == targetY)
                .Where(e => sourceVillageId is null || e.VillageId == sourceVillageId.Value)
                .ToList();
        }

        // True when the matching rows come from more than one village - the report alone (no
        // attacker village in the list row) cannot say which of them it belongs to.
        public static bool SpansSeveralVillages(IReadOnlyCollection<RaidListEntry> matches)
        {
            return matches.Select(e => e.VillageId).Distinct().Count() > 1;
        }

        // Which of OUR villages sent a report, and the map radius that goes with it: the one
        // whose (x|y) has exactly this tile id for some radius (see MapTiles). Null when none
        // or more than one village fits - a guess here would apply a report to the wrong row.
        public static (int VillageId, int Radius)? ResolveAttackerVillage(int attackerTileId, IEnumerable<(int VillageId, int X, int Y)> villages)
        {
            var matches = new List<(int VillageId, int Radius)>();
            foreach (var (villageId, x, y) in villages)
            {
                var radius = MapTiles.InferRadius(attackerTileId, x, y);
                if (radius is not null) matches.Add((villageId, radius.Value));
            }

            return matches.Count == 1 ? matches[0] : null;
        }
    }
}
