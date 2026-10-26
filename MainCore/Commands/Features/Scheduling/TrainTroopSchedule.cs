using System.Collections.Concurrent;

namespace MainCore.Commands.Features.Scheduling
{
    // 2026-10-26, user decision: every training building has its own "not again before" interval, and a
    // building is NOT visited earlier than that even when its queue ends sooner. The task wakes up for
    // whichever building is due first, so the per-building "not before" time is remembered here (in
    // memory only: after a restart every building is simply due again, which is the old behaviour).
    public static class TrainTroopSchedule
    {
        private static readonly ConcurrentDictionary<(int VillageId, BuildingEnums Building), DateTime> NotBefore = new();

        public static DateTime GetNotBefore(VillageId villageId, BuildingEnums building)
        {
            return NotBefore.TryGetValue((villageId.Value, building), out var time) ? time : DateTime.MinValue;
        }

        public static void Set(VillageId villageId, BuildingEnums building, DateTime notBefore)
        {
            NotBefore[(villageId.Value, building)] = notBefore;
        }

        public static void Clear(VillageId villageId)
        {
            foreach (var key in NotBefore.Keys.Where(x => x.VillageId == villageId.Value).ToList())
            {
                NotBefore.TryRemove(key, out _);
            }
        }
    }
}
