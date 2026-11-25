using System.Collections.Concurrent;

namespace MainCore.Commands.Features.Scheduling
{
    // 2026-11-07 ("construction reserve" village setting, see ConstructionReserveRules): what the village's build
    // plan needs, as seen by UpgradeBuildingTask / HandleResourceCommand, for TrainTroopTask to read.
    //   - Cost: wood / clay / iron / crop of the building the plan is about to build (read from the build page).
    //   - SlotBusyUntil: the game's build queue is full until this time (the next building cannot start before).
    // Memory only: after a restart nothing is known until UpgradeBuildingTask runs again, and training then only
    // keeps the percent part of the floor. Both values expire on their own (a stale cost must never hold the
    // troops forever), and UpgradeBuildingTask clears them when nothing is waiting.
    public static class ConstructionNeed
    {
        public const int CostValidHours = 6;

        private sealed record CostEntry(long[] Cost, DateTime Time);

        private static readonly ConcurrentDictionary<int, CostEntry> Costs = new();
        private static readonly ConcurrentDictionary<int, DateTime> BusyUntil = new();

        public static void SetCost(VillageId villageId, long[] cost)
        {
            if (cost is null || cost.Length < 4) return;
            Costs[villageId.Value] = new CostEntry([cost[0], cost[1], cost[2], cost[3]], DateTime.Now);
        }

        public static long[]? GetCost(VillageId villageId)
        {
            if (!Costs.TryGetValue(villageId.Value, out var entry)) return null;
            if (entry.Time.AddHours(CostValidHours) < DateTime.Now) return null;
            return entry.Cost;
        }

        // 2026-11-25: the cost of the job UpgradeBuildingTask is WAITING for (resources missing), valid until the planned
        // next look plus a grace period. Unlike GetCost it is not set for a job that was just built, so a shipping task
        // (Overflow to hammer, Balance) can keep exactly this much without a stale floor. Self-expiring.
        public const int WaitingGraceMinutes = 20;

        private sealed record WaitEntry(long[] Cost, DateTime Until);

        private static readonly ConcurrentDictionary<int, WaitEntry> Waiting = new();

        public static void SetWaiting(VillageId villageId, long[] cost, DateTime nextLook)
        {
            if (cost is null || cost.Length < 4 || !cost.Take(4).Any(x => x > 0)) return;
            Waiting[villageId.Value] = new WaitEntry([cost[0], cost[1], cost[2], cost[3]], nextLook.AddMinutes(WaitingGraceMinutes));
        }

        public static long[]? GetWaiting(VillageId villageId)
        {
            if (!Waiting.TryGetValue(villageId.Value, out var entry)) return null;
            if (entry.Until < DateTime.Now) return null;
            return entry.Cost;
        }

        public static void ClearWaiting(VillageId villageId)
        {
            Waiting.TryRemove(villageId.Value, out _);
        }

        public static void SetSlotBusyUntil(VillageId villageId, DateTime until)
        {
            BusyUntil[villageId.Value] = until;
        }

        // How long the build queue is still full; null = not known to be full.
        public static TimeSpan? GetSlotBusyFor(VillageId villageId)
        {
            if (!BusyUntil.TryGetValue(villageId.Value, out var until)) return null;
            var left = until - DateTime.Now;
            return left > TimeSpan.Zero ? left : null;
        }

        public static void ClearSlotBusy(VillageId villageId)
        {
            BusyUntil.TryRemove(villageId.Value, out _);
        }

        public static void Clear(VillageId villageId)
        {
            Costs.TryRemove(villageId.Value, out _);
            BusyUntil.TryRemove(villageId.Value, out _);
            Waiting.TryRemove(villageId.Value, out _);
        }
    }
}
