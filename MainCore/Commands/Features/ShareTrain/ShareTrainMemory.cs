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

        public static void Clear(int villageId) => NextCheck.TryRemove(villageId, out _);
    }
}
