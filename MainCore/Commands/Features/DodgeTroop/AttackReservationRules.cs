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
    // pure function (no BaseTask/DI) and can be unit tested.
    //  * Exempt   = may run during a reservation.
    //  * Priority = a defensive/session task (Dodge, Defend, Recall, Login, WakeUp) that jumps ahead
    //               of ordinary due tasks the moment it is due, reservation or not. Needed because the
    //               queue is sorted by ExecuteAt: tasks held back during a reservation have OLDER
    //               times and would otherwise run first and push a time-critical Recall (90 s cancel
    //               window) or a freshly queued Dodge behind them (log 2026-10-20 11:30-11:31).
    public readonly record struct ReservationQueueEntry(DateTime ExecuteAt, bool Exempt, bool Priority = false);

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
        //  1. The first DUE Priority entry always wins (see ReservationQueueEntry).
        //  2. No reservation covers "now": otherwise the old rule - only the head, and only when due.
        //  3. Inside a reservation: otherwise the first DUE exempt entry. Held-back tasks stay queued
        //     and become runnable again once the reservation is over, so they simply run after the attack.
        public static int SelectIndex(IReadOnlyList<ReservationQueueEntry> queue, DateTime now, IReadOnlyList<AttackReservation> reservations)
        {
            if (queue.Count == 0) return -1;

            for (var i = 0; i < queue.Count; i++)
            {
                if (queue[i].ExecuteAt > now) break;
                if (queue[i].Priority) return i;
            }

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
