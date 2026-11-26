namespace MainCore.Commands.Features.OasisScout
{
    // Pure rules of Oasis scout (no browser, no database) so they can be tested in the pure harness.
    public static class OasisScoutRules
    {
        // 2026-11-25: a run that finds no usable oasis report costs two page loads in the single account queue
        // (every 12-15 minutes in the 2026-11-25 log). The surrounding page only changes when somebody plunders
        // an oasis nearby, so an empty run waits at least this long (the configured interval still wins when longer).
        public const int EmptyRunMinMinutes = 30;
        public const int EmptyRunJitterMaxMinutes = 10;

        public static DateTime ParkEmptyRun(DateTime now, DateTime scheduled, int jitterMinutes)
        {
            var jitter = Math.Clamp(jitterMinutes, 0, EmptyRunJitterMaxMinutes);
            var earliest = now.AddMinutes(EmptyRunMinMinutes + jitter);
            return scheduled > earliest ? scheduled : earliest;
        }
    }
}
