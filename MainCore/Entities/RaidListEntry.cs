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
    // StartFarmListTask), every row here is scheduled completely independently by
    // RaidListTask: after each send, NextExecuteAt is set to
    // now + random(IntervalMinMinutes, IntervalMaxMinutes), so a 100-row list ends up staggered
    // rather than firing in one batch.
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

