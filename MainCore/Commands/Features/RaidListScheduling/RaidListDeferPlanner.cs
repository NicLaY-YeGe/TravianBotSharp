namespace MainCore.Commands.Features.RaidListScheduling
{
    // Pure calculation, no browser/DB access - kept out of RaidListTask so it can be unit tested
    // directly (same rationale as WaveAttackPlanner / RaidReportRules).
    //
    // Problem it solves (2026-09-20, live log): every Raid List row that found the account-wide
    // send gate still closed re-parked itself at the EXACT gate time. All of them therefore woke
    // in the same second; one sent, the gate closed again, and the other ~25 ran a full (useless)
    // task cycle and re-parked at the new gate - a thundering herd that repeated at every gate.
    //
    // Instead each deferred row now takes the next free slot of a queue that starts at the gate:
    // the first deferred row gets the gate time itself, the next one gets (previous slot + a
    // random gap), and so on, spaced by the same random(min, max) SECONDS range the gate uses.
    public static class RaidListDeferPlanner
    {
        // gateTime      - earliest moment any row may send (the account-wide gate).
        // lastSlot      - the last slot handed out by this planner for the account, if any.
        // activeRows    - how many active rows the account has; bounds how long a real queue can be.
        // gapMin/MaxSec - the account's RaidListSendGapMin/MaxSeconds.
        public static DateTime NextSlot(DateTime gateTime, DateTime? lastSlot, int activeRows, int gapMinSeconds, int gapMaxSeconds, Random random)
        {
            var min = Math.Max(1, gapMinSeconds);
            var max = Math.Max(min, gapMaxSeconds);

            // Queue is empty (nothing reserved, or every reserved slot is already behind the gate).
            if (lastSlot is null || lastSlot.Value < gateTime) return gateTime;

            // Stale tail: a real queue holds at most one slot per active row, each at most `max`
            // seconds after the previous one. A tail further out than that belongs to rows that
            // were since paused/deleted (the last slot is kept in memory only) - start over.
            var longestPossibleQueue = Math.Max(1, activeRows) * (double)max;
            if ((lastSlot.Value - gateTime).TotalSeconds > longestPossibleQueue) return gateTime;

            return lastSlot.Value.AddSeconds(random.Next(min, max + 1));
        }
    }
}
