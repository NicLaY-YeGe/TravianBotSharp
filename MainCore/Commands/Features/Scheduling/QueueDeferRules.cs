namespace MainCore.Commands.Features.Scheduling
{
    // 2026-10-24, user decision: tasks whose game-side queue has a known end (training queue,
    // trap queue, smithy research, celebration) wait for that end instead of re-opening the page
    // every cycle, plus a small random buffer (30-60 s) so the visit never lands a moment BEFORE
    // the queue is really finished. Pure so it is unit tested.
    //
    // 2026-10-25: the per-task decisions (which wait applies when) live here as well, so the tasks
    // only read the page and hand the numbers over.
    public static class QueueDeferRules
    {
        public const int BufferMinSeconds = 30;
        public const int BufferMaxSeconds = 60;

        // Training / trap queue timers come from a parser that has not been checked against a real
        // capture of an ACTIVE queue yet (see TrainQueueParser), so a wrong value must never park a
        // task for long: such waits are capped, the worst case is one extra visit.
        public const int UncheckedQueueCapSeconds = 3 * 60 * 60;

        // A trapper that is at its level's cap has no queue end: the cap only changes when the
        // building is upgraded or traps are used up, so it is looked at again after this long
        // (plus up to TrapAtCapJitterMinutes so the visits do not fall on a fixed clock).
        public const int TrapAtCapRecheckMinutes = 120;
        public const int TrapAtCapJitterMinutes = 10;

        // Smithy / Town Hall: the page shows no running research / celebration AND no Improve / Hold
        // button, so the reason (resources short, level too low, already maxed...) is not readable.
        // Before 2026-10-25 this was a fixed 20 minutes, which showed in the log as a visit every
        // 20-22 minutes per village; now 45-60 minutes, random.
        public const int UnavailableRecheckMinMinutes = 45;
        public const int UnavailableRecheckMaxMinutes = 60;

        public static int BufferedSeconds(int secondsRemaining, Random random)
        {
            return Math.Max(0, secondsRemaining) + random.Next(BufferMinSeconds, BufferMaxSeconds + 1);
        }

        public static int BufferedSecondsCapped(int secondsRemaining, Random random, int capSeconds = UncheckedQueueCapSeconds)
        {
            return Math.Min(BufferedSeconds(secondsRemaining, random), capSeconds);
        }

        public static int UnavailableRecheckSeconds(Random random)
        {
            return random.Next(UnavailableRecheckMinMinutes * 60, UnavailableRecheckMaxMinutes * 60 + 1);
        }

        // Smithy / celebration: seconds until the next visit. A running research / celebration
        // that could be read wins (its exact end + buffer, no cap - those timers come from real
        // captures); anything else is the "reason unknown" recheck.
        public static int BusySeconds(int? runningSecondsRemaining, Random random)
        {
            return runningSecondsRemaining is > 0
                ? BufferedSeconds(runningSecondsRemaining.Value, random)
                : UnavailableRecheckSeconds(random);
        }

        // A parse that failed reads as max = 0; that must never count as "at cap" (it would park the
        // task for two hours on a half-loaded page).
        public static bool IsTrapperAtCap(int maxPossibleTraps, int currentTraps)
        {
            return maxPossibleTraps > 0 && currentTraps >= maxPossibleTraps;
        }

        public static int TrapAtCapSeconds(Random random)
        {
            return TrapAtCapRecheckMinutes * 60 + random.Next(0, TrapAtCapJitterMinutes * 60 + 1);
        }

        // Trapper: at cap -> long recheck; a readable running queue -> its end (+ buffer, capped);
        // otherwise (resources short, nothing readable) the user's normal repeat time.
        public static int NextTrapSeconds(int? queueSecondsRemaining, bool atCap, int normalRepeatSeconds, Random random)
        {
            if (atCap) return TrapAtCapSeconds(random);
            if (queueSecondsRemaining is > 0) return BufferedSecondsCapped(queueSecondsRemaining.Value, random);
            return normalRepeatSeconds;
        }

        // Troop training: one entry per building that was looked at - the seconds left on its running
        // queue, or null when nothing is known (not enough resources, queue unreadable). A building
        // with a known queue is revisited at that end (+ buffer, capped); a building with nothing known
        // keeps the user's normal repeat time. The EARLIEST of them wins, so a busy Barracks never
        // delays the retry of a starved Stable.
        public static int NextTrainSeconds(IReadOnlyCollection<int?> queueSecondsPerBuilding, int normalRepeatSeconds, Random random)
        {
            if (queueSecondsPerBuilding.Count == 0) return normalRepeatSeconds;

            var best = int.MaxValue;
            foreach (var queueSeconds in queueSecondsPerBuilding)
            {
                var candidate = queueSeconds is > 0
                    ? BufferedSecondsCapped(queueSeconds.Value, random)
                    : normalRepeatSeconds;
                best = Math.Min(best, candidate);
            }

            return best;
        }
    }
}
