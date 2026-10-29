using System.Collections.Concurrent;

namespace MainCore.Commands.Features.Scheduling
{
    // 2026-10-26, user decision ("construction first" village setting): UpgradeBuildingTask notes here
    // that a build plan is waiting for resources and until when it will look again. TrainTroopTask of
    // the same village trains nothing while the hold lasts, so the troops do not eat the resources the
    // building is waiting for. Memory only; the hold always expires on its own (MaxHoldHours), and
    // UpgradeBuildingTask clears it as soon as a plan completes or there is nothing waiting.
    public static class ConstructionHold
    {
        public const int MaxHoldHours = 3;

        private static readonly ConcurrentDictionary<int, DateTime> Until = new();

        public static void Set(VillageId villageId, DateTime untilTime)
        {
            var cap = DateTime.Now.AddHours(MaxHoldHours);
            Until[villageId.Value] = untilTime > cap ? cap : untilTime;
        }

        public static void Clear(VillageId villageId)
        {
            Until.TryRemove(villageId.Value, out _);
        }

        public static DateTime? GetUntil(VillageId villageId)
        {
            return Until.TryGetValue(villageId.Value, out var time) && time > DateTime.Now ? time : null;
        }
    }
}
