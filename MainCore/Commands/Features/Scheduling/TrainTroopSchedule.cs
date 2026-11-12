using System.Collections.Concurrent;

namespace MainCore.Commands.Features.Scheduling
{
    // 2026-10-26, user decision: every training building has its own "not again before" interval, and a
    // building is NOT visited earlier than that even when its queue ends sooner. 2026-10-27: the
    // interval belongs to each TROOP (a building trains several troops, each with its own time), so the
    // key is (village, building, troop); TroopEnums.None = the building as a whole (old single-troop
    // settings). The task wakes up for whichever troop is due first, so the "not before" time is
    // remembered here (in memory only: after a restart every troop is simply due again, which is the old
    // behaviour).
    public static class TrainTroopSchedule
    {
        private static readonly ConcurrentDictionary<(int VillageId, BuildingEnums Building, TroopEnums Troop), DateTime> NotBefore = new();

        public static DateTime GetNotBefore(VillageId villageId, BuildingEnums building, TroopEnums troop = TroopEnums.None)
        {
            return NotBefore.TryGetValue((villageId.Value, building, troop), out var time) ? time : DateTime.MinValue;
        }

        public static void Set(VillageId villageId, BuildingEnums building, DateTime notBefore, TroopEnums troop = TroopEnums.None)
        {
            NotBefore[(villageId.Value, building, troop)] = notBefore;
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
