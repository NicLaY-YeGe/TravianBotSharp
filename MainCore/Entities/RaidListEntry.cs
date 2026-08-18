using StronglyTypedIds;
using System.Text.Json;

#nullable disable

namespace MainCore.Entities
{
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
        public string TroopAmountsJson { get; set; }

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
    }

    [StronglyTypedId]
    public partial struct RaidListEntryId
    { }
}
