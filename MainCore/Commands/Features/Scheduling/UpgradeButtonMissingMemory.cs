using System.Collections.Concurrent;

namespace MainCore.Commands.Features.Scheduling
{
    // 2026-11-17: counts, per village, how many runs in a row found no Upgrade button (see UpgradeWaitRules.ButtonMissingWait).
    // Memory only; reset by every successful upgrade of the village.
    public static class UpgradeButtonMissingMemory
    {
        private static readonly ConcurrentDictionary<int, int> Misses = new();

        public static int Add(VillageId villageId)
        {
            return Misses.AddOrUpdate(villageId.Value, 1, (_, count) => count + 1);
        }

        public static void Reset(VillageId villageId)
        {
            Misses.TryRemove(villageId.Value, out _);
        }
    }
}
