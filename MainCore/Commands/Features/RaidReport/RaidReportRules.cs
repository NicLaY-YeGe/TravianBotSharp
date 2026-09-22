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

        // 2026-09-21, user request: auto-scale the troop amount sent to a target by how full
        // its carry capacity has been coming back. Deliberately a "streak just reached N"
        // trigger (==, not >=) rather than "adjust every report past N" - the streak keeps
        // counting past the threshold and re-triggering every report would compound every
        // single time (e.g. x0.8 x0.8 x0.8 ... after 10 low raids in a row), which is a much
        // more aggressive curve than "rough, one-step-at-a-time" calls for.
        public const int LowLootStreakToShrink = 3;
        public const int FullLootStreakToGrow = 2;

        // How much one shrink/grow step moves the multiplier, and the floor/ceiling it can
        // never cross. A shrink never goes below MinTroopMultiplierPercent no matter how long
        // the low-loot streak still runs - this multiplier alone never empties a row down to
        // zero troops, and it does not pause anything (GetPauseReason below is unrelated,
        // loot-percent-only, and untouched by this).
        public const int ShrinkStepPercent = 20;
        public const int GrowStepPercent = 25;
        public const int MinTroopMultiplierPercent = 40;
        public const int MaxTroopMultiplierPercent = 200;

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

            var multiplier = NextTroopMultiplierPercent(previous.EffectiveTroopMultiplierPercent, lowLoot, fullLoot);

            return new RaidReportStats(reportId, outcome, lootPercent, consecutiveLossy, lowLoot, fullLoot, previous.ReportCount + 1, multiplier);
        }

        // The troop multiplier moves by one step exactly when a streak reaches its threshold -
        // a streak that later resets to 0 (a normal-loot report breaks it) never un-does a step
        // already taken; the multiplier only moves again on the NEXT streak reaching threshold.
        // If both streaks reached their threshold on the same report (impossible today - low
        // and full are mutually exclusive on any single lootPercent value - but kept safe for
        // future loot bands) shrink wins, since sending fewer troops is the safer mistake.
        public static int NextTroopMultiplierPercent(int currentMultiplierPercent, int lowLootStreak, int fullLootStreak)
        {
            var next = currentMultiplierPercent;

            if (lowLootStreak == LowLootStreakToShrink)
            {
                next = next * (100 - ShrinkStepPercent) / 100;
            }
            else if (fullLootStreak == FullLootStreakToGrow)
            {
                next = next * (100 + GrowStepPercent) / 100;
            }

            return Math.Clamp(next, MinTroopMultiplierPercent, MaxTroopMultiplierPercent);
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

        // Short text for the Raid List row, e.g. "94% (full x2)" or "19% (low x3, troops 80%)";
        // empty until a report has been read.
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
            if (stats.EffectiveTroopMultiplierPercent != 100) notes.Add($"troops {stats.EffectiveTroopMultiplierPercent}%");

            return notes.Count == 0 ? basis : $"{basis} ({string.Join(", ", notes)})";
        }

        // Scales one troop-amount range by the row's current multiplier, keeping Min<=Max and
        // never scaling a real positive amount down to 0 - a shrunk range still sends SOME
        // troops (a target worth shrinking for is still worth raiding, just lighter); stopping
        // entirely is GetPauseReason's job, not this one's. A configured 0 (slot not used)
        // stays 0.
        public static TroopAmountRange ScaleRange(TroopAmountRange range, int multiplierPercent)
        {
            var min = ScaleAmount(range.Min, multiplierPercent);
            var max = ScaleAmount(range.Max, multiplierPercent);
            if (max < min) max = min;

            return new TroopAmountRange(min, max);
        }

        private static long ScaleAmount(long amount, int multiplierPercent)
        {
            if (amount <= 0) return amount;

            var scaled = amount * multiplierPercent / 100;
            return Math.Max(1, scaled);
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
