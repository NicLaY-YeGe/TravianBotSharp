using System.Collections.Concurrent;

namespace MainCore.Commands.Features.DodgeTroop
{
    // In-memory registry of the attacks whose time is currently reserved for Dodge (see
    // AttackReservationRules), per account + village. Filled by DodgeTroopTask / DefendDonateTask
    // the moment they read the real impact time off the Rally Point, read by TimerManager every
    // tick. In memory on purpose (like DefendAttackMemory): after an app restart the next
    // Rally Point read simply registers it again.
    public static class AttackReservationStore
    {
        // The Rally Point countdown is re-read several times for the same attack; reads differ by
        // a second or two. Within this tolerance it is the SAME attack and keeps its random lead.
        private const int SameAttackToleranceSeconds = 60;

        private static readonly ConcurrentDictionary<(int AccountId, int VillageId), AttackReservation> Items = new();

        public static (AttackReservation Reservation, bool IsNew) Register(int accountId, int villageId, DateTime impactAt, DateTime now, Random random)
        {
            Prune(now);

            var key = (accountId, villageId);
            if (Items.TryGetValue(key, out var existing)
                && Math.Abs((existing.ImpactAt - impactAt).TotalSeconds) <= SameAttackToleranceSeconds)
            {
                var updated = existing with { ImpactAt = impactAt };
                Items[key] = updated;
                return (updated, false);
            }

            var created = new AttackReservation(impactAt, AttackReservationRules.RandomLeadSeconds(random));
            Items[key] = created;
            return (created, true);
        }

        // Reservations of this account that have not ended yet (including ones that start later).
        public static IReadOnlyList<AttackReservation> GetActive(int accountId, DateTime now)
        {
            Prune(now);
            return Items.Where(x => x.Key.AccountId == accountId).Select(x => x.Value).ToList();
        }

        // The attack is gone (landed, cancelled, or the flag was stale): stop holding tasks back.
        public static void Clear(int accountId, int villageId) => Items.TryRemove((accountId, villageId), out _);

        public static void ClearAll() => Items.Clear();

        private static void Prune(DateTime now)
        {
            foreach (var (key, value) in Items)
            {
                if (value.ReservedUntil <= now) Items.TryRemove(key, out _);
            }
        }
    }
}
