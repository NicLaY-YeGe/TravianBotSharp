namespace MainCore.Commands.Features.DodgeTroop
{
    // One incoming attack's "reserved" time: from LeadSeconds before impact until a few seconds
    // after it, the single-threaded queue belongs to the defensive tasks (Dodge, Defend, Recall,
    // Login, WakeUp); every other task waits and runs after the attack (user decision 2026-10-19:
    // "saldırıdan 3-5 dk önceki görevler saldırı sonrasına gitsin, bu süre dodge için ayrılsın").
    public sealed record AttackReservation(DateTime ImpactAt, int LeadSeconds)
    {
        public DateTime ReservedFrom => ImpactAt.AddSeconds(-LeadSeconds);
        public DateTime ReservedUntil => ImpactAt.AddSeconds(AttackReservationRules.AfterImpactSeconds);

        public bool Covers(DateTime now) => now >= ReservedFrom && now < ReservedUntil;
    }

    // One queue entry as TimerManager sees it - just what the selection needs, so the rule is a
    // pure function (no BaseTask/DI) and can be unit tested. Exempt = may run during a reservation.
    public readonly record struct ReservationQueueEntry(DateTime ExecuteAt, bool Exempt);

    public static class AttackReservationRules
    {
        // The reserved lead is a random pick in [Min, Max] seconds per attack (3-5 minutes).
        public const int MinLeadSeconds = 180;
        public const int MaxLeadSeconds = 300;

        // The reservation outlives impact by this long, then the held-back tasks resume.
        public const int AfterImpactSeconds = 15;

        public static int RandomLeadSeconds(Random random) => random.Next(MinLeadSeconds, MaxLeadSeconds + 1);

        public static bool IsReserved(IReadOnlyList<AttackReservation> reservations, DateTime now)
            => reservations.Any(r => r.Covers(now));

        // Which queue entry TimerManager should run now (-1 = none). The queue is sorted by ExecuteAt.
        //  * No reservation covers "now": exactly the old rule - only the head, and only when due.
        //  * Inside a reservation: the first DUE entry that is exempt. Held-back tasks stay queued
        //    and become the head again once the reservation is over, so they simply run after the attack.
        public static int SelectIndex(IReadOnlyList<ReservationQueueEntry> queue, DateTime now, IReadOnlyList<AttackReservation> reservations)
        {
            if (queue.Count == 0) return -1;

            if (!IsReserved(reservations, now)) return queue[0].ExecuteAt <= now ? 0 : -1;

            for (var i = 0; i < queue.Count; i++)
            {
                var entry = queue[i];
                if (entry.ExecuteAt > now) break;
                if (entry.Exempt) return i;
            }

            return -1;
        }
    }
}
