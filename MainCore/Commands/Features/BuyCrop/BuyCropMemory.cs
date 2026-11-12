using System.Collections.Concurrent;

namespace MainCore.Commands.Features.BuyCrop
{
    // 2026-11-10: memory only (like ConstructionNeed / SupplyBackoff), keyed by the village's database id.
    //   - Pending: crop bought on the Marketplace that is still travelling. It counts as stock for the trigger, so a
    //     trip of several hours does not make the bot buy again every few minutes.
    //   - NextCheck: "not before" time. The task ends with ExecuteAt unchanged (TimerManager removes it) and
    //     UpdateStorageCommand queues it again after every storage refresh, so CanStart must hold this back or an
    //     empty offer list would mean a Marketplace visit every minute.
    // A Restart / app restart forgets both (accepted: at worst one more purchase while the first is still travelling).
    public static class BuyCropMemory
    {
        private sealed record Entry(long Amount, DateTime Arrival);

        private static readonly ConcurrentDictionary<int, List<Entry>> Pending = new();
        private static readonly ConcurrentDictionary<int, DateTime> NextCheck = new();

        public static void AddPending(int villageId, long amount, DateTime arrival)
        {
            if (amount <= 0) return;
            var list = Pending.GetOrAdd(villageId, _ => new List<Entry>());
            lock (list)
            {
                list.Add(new Entry(amount, arrival));
            }
        }

        // Crop still on its way at `now`; delivered entries are dropped.
        public static long PendingAmount(int villageId, DateTime? now = null)
        {
            if (!Pending.TryGetValue(villageId, out var list)) return 0;
            var time = now ?? DateTime.Now;
            lock (list)
            {
                list.RemoveAll(x => x.Arrival <= time);
                return list.Sum(x => x.Amount);
            }
        }

        public static void SetNextCheck(int villageId, DateTime time) => NextCheck[villageId] = time;

        public static bool IsDue(int villageId, DateTime? now = null)
        {
            if (!NextCheck.TryGetValue(villageId, out var time)) return true;
            return time <= (now ?? DateTime.Now);
        }

        public static void Clear(int villageId)
        {
            Pending.TryRemove(villageId, out _);
            NextCheck.TryRemove(villageId, out _);
        }
    }
}
