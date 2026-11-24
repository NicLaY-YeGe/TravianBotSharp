using System.Collections.Concurrent;

namespace MainCore.Commands.Features.ShareTrain
{
    // 2026-11-13: memory only (like BuyCropMemory), keyed by the village's database id. The share task ends with
    // ExecuteAt unchanged (TimerManager removes it) and UpdateStorageCommand queues it again after every storage
    // refresh, so CanStart must hold it back for a few minutes or every page read would start another visit.
    // A Restart / app restart forgets it (harmless: one visit earlier than planned).
    public static class ShareTrainMemory
    {
        private static readonly ConcurrentDictionary<int, DateTime> NextCheck = new();

        public static void SetNextCheck(int villageId, DateTime time) => NextCheck[villageId] = time;

        public static bool IsDue(int villageId, DateTime? now = null)
        {
            if (!NextCheck.TryGetValue(villageId, out var time)) return true;
            return time <= (now ?? DateTime.Now);
        }

        public static void Clear(int villageId)
        {
            NextCheck.TryRemove(villageId, out _);
            foreach (var key in UnitCosts.Keys.Where(x => x.Village == villageId)) UnitCosts.TryRemove(key, out _);
        }

        // 2026-11-15: unit cost of every weighted troop, remembered from the last survey so a visit that could not train a
        // single unit (nothing affordable above the reserve, e.g. iron at 0) does not open the buildings at all. A cost is
        // constant in Travian, so it is kept for 12 h; "not trainable right now" (not researched) is kept for 1 h and is
        // stored as an empty array.
        private static readonly ConcurrentDictionary<(int Village, int Troop), (long[] Cost, DateTime Until)> UnitCosts = new();

        public static readonly TimeSpan CostKeep = TimeSpan.FromHours(12);
        public static readonly TimeSpan UnavailableKeep = TimeSpan.FromHours(1);

        public static void SetUnitCost(int villageId, int troop, long[] cost, DateTime now) => UnitCosts[(villageId, troop)] = (cost, now + CostKeep);

        public static void SetUnavailable(int villageId, int troop, DateTime now) => UnitCosts[(villageId, troop)] = (Array.Empty<long>(), now + UnavailableKeep);

        // null = unknown (never surveyed or expired); empty array = not trainable; otherwise the unit cost.
        public static long[]? GetUnitCost(int villageId, int troop, DateTime? now = null)
        {
            if (!UnitCosts.TryGetValue((villageId, troop), out var entry)) return null;
            return entry.Until > (now ?? DateTime.Now) ? entry.Cost : null;
        }
    }
}
