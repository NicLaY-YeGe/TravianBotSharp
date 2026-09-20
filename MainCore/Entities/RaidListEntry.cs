using StronglyTypedIds;
using System.Text.Json;

#nullable disable

namespace MainCore.Entities
{
    // A troop slot's send amount as an inclusive random range - Min==Max behaves exactly like
    // the old fixed-amount shape. Deliberately a plain record (not a ValueTuple) so it
    // round-trips through System.Text.Json as {"Min":2,"Max":6} instead of {"Item1":2,"Item2":6}.
    public record TroopAmountRange(long Min, long Max);

    // One row of the (bot-side, non-native) Raid List: a source village, a target coordinate,
    // a troop composition to send there as a raid (RallyPointEventTypeEnums.AttackRaid), and
    // this row's OWN randomized resend interval - unlike Travian's native Farm List (which is a
    // gold-only feature and fires the whole list together on one shared interval, see
    // StartFarmListTask), every row here has its own candidate schedule in RaidListTask: after
    // each send, NextExecuteAt is set to now + random(IntervalMinMinutes, IntervalMaxMinutes).
    // BUT (2026-09-16) an actual send is also gated behind a single account-wide "not before"
    // timestamp shared by every row of the account (see
    // AccountSettingEnums.RaidListNextAllowedSendAtMinutes / RaidListTask's class comment) - so
    // a big list doesn't fire several rows within seconds of each other just because their
    // independent random offsets happened to land close together.
    public class RaidListEntry
    {
        public int Id { get; set; }

        public int AccountId { get; set; }

        // Source village - where the troops are sent FROM (this village's Rally Point).
        public int VillageId { get; set; }

        public int TargetX { get; set; }
        public int TargetY { get; set; }

        // JSON-serialized IReadOnlyDictionary<int,long> (rally point troop slot -> amount),
        // e.g. {"1":500,"3":200}. Stored as JSON rather than a normalized child table to match
        // how SendTroopsCommand already consumes this shape (TroopAmounts parameter) without
        // needing a DTO/mapper round-trip - see GetTroopAmounts/SetTroopAmounts below.
        //
        // Superseded by TroopAmountRangesJson below (2026-08-22) for newly-created rows - kept
        // only so GetTroopAmountRanges() can fall back to it for rows created before that date
        // (this project has no EF Core migrations, see AppDbContext's "schema patches" region,
        // so an old row's data can't be rewritten in place without a one-off script; falling
        // back in code is simpler and doesn't require one).
        public string TroopAmountsJson { get; set; }

        // JSON-serialized IReadOnlyDictionary<int,TroopAmountRange> (rally point troop slot ->
        // inclusive min/max), e.g. {"2":{"Min":2,"Max":6},"11":{"Min":1,"Max":1}}. RaidListTask
        // rolls a fresh Dictionary<int,long> from this on every send (see RollTroopAmounts) -
        // unlike TroopAmountsJson above, the amount actually sent varies run to run, matching a
        // human raiding by hand rather than sending the exact same stack every time.
        public string TroopAmountRangesJson { get; set; }

        public bool IncludeHero { get; set; }

        public int IntervalMinMinutes { get; set; }
        public int IntervalMaxMinutes { get; set; }

        // Persisted (not just held in the in-memory RaidListTask.Task) so a restart doesn't
        // reset every row to "due immediately" - the bootstrap check in UpdateStorageCommand
        // re-adds a task at this exact time if one isn't already queued.
        public DateTime NextExecuteAt { get; set; }

        public bool IsActive { get; set; }

        // 2026-09-19 (RaidReportTask, "Yagma organize" scope A): JSON-serialized
        // RaidReportStats - what the last raid reports for this row's target said (outcome,
        // loot percent, streaks). Null until the first matching report is read. A NEW COLUMN on
        // an existing table, so it needs the hand-written ALTER TABLE patch in AppDbContext
        // (EnsureRaidListEntriesReportStatsColumnExists) - see that method's comment.
        public string ReportStatsJson { get; set; }

        // 2026-09-20, user request: the server answered "There is no village at these
        // coordinates." for this row's target (abandoned/conquered). The row is KEPT (inactive)
        // instead of deleted so the coordinate is remembered: RaidListTask skips - without
        // touching the game and without stopping the bot - any row, old or newly entered, whose
        // target matches a dead row of the same account. Deleting every dead row for a
        // coordinate from the Raid List tab makes the bot forget it. A NEW COLUMN, so it needs
        // the hand-written ALTER TABLE patch (EnsureRaidListEntriesDeadTargetColumnExists).
        public bool IsDeadTarget { get; set; }

        public RaidReportStats GetReportStats()
        {
            if (string.IsNullOrWhiteSpace(ReportStatsJson)) return RaidReportStats.Empty;

            try
            {
                return JsonSerializer.Deserialize<RaidReportStats>(ReportStatsJson) ?? RaidReportStats.Empty;
            }
            catch (JsonException)
            {
                // Statistics only - a corrupt value must never break the Raid List itself.
                return RaidReportStats.Empty;
            }
        }

        public void SetReportStats(RaidReportStats stats)
        {
            ReportStatsJson = JsonSerializer.Serialize(stats);
        }

        public IReadOnlyDictionary<int, long> GetTroopAmounts()
        {
            if (string.IsNullOrWhiteSpace(TroopAmountsJson)) return new Dictionary<int, long>();
            return JsonSerializer.Deserialize<Dictionary<int, long>>(TroopAmountsJson) ?? new Dictionary<int, long>();
        }

        public void SetTroopAmounts(IReadOnlyDictionary<int, long> amounts)
        {
            TroopAmountsJson = JsonSerializer.Serialize(amounts);
        }

        public IReadOnlyDictionary<int, TroopAmountRange> GetTroopAmountRanges()
        {
            if (!string.IsNullOrWhiteSpace(TroopAmountRangesJson))
            {
                return JsonSerializer.Deserialize<Dictionary<int, TroopAmountRange>>(TroopAmountRangesJson)
                    ?? new Dictionary<int, TroopAmountRange>();
            }

            // Back-compat: a row created before 2026-08-22 only has the old fixed-amount
            // column. Treat each fixed amount as a zero-width range (Min == Max) so nothing
            // changes in behavior for existing rows until the user edits them.
            return GetTroopAmounts().ToDictionary(kv => kv.Key, kv => new TroopAmountRange(kv.Value, kv.Value));
        }

        public void SetTroopAmountRanges(IReadOnlyDictionary<int, TroopAmountRange> ranges)
        {
            TroopAmountRangesJson = JsonSerializer.Serialize(ranges);
        }

        // Rolls a fresh amount for every slot, independently, uniformly within [Min, Max]
        // inclusive (Min == Max just returns Min - no randomness needed). Called once per
        // RaidListTask run and the SAME result is used for both the pre-send availability
        // check and the actual send, so what gets checked is exactly what gets sent.
        public IReadOnlyDictionary<int, long> RollTroopAmounts(Random random)
        {
            var ranges = GetTroopAmountRanges();
            var result = new Dictionary<int, long>(ranges.Count);
            foreach (var (slot, range) in ranges)
            {
                var min = Math.Max(0, range.Min);
                var max = Math.Max(min, range.Max);
                result[slot] = max == min ? min : random.NextInt64(min, max + 1);
            }
            return result;
        }
    }

    [StronglyTypedId]
    public partial struct RaidListEntryId
    { }
}

