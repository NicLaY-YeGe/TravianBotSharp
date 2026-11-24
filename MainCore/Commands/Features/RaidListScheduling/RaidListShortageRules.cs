using System.Collections.Concurrent;

namespace MainCore.Commands.Features.RaidListScheduling
{
    // 2026-11-11 (live log 2026-11-11 00:27: "doesn't have enough troops in slot 4 (needs 43, has 28) - pausing the
    // whole raid list"; nothing switched it on again, so the raid list stayed off for 5+ hours): running short of troops
    // right now is routine on an unattended list (the previous wave has not come home, the village has two rows for
    // one set of troops). The 2026-08-25 behaviour paused EVERY row for good on the first shortage.
    //
    // Now only the row that found too few troops waits a few minutes and tries again. The user's original wish - being
    // alerted - is kept for a shortage that does not go away: after MaxConsecutiveBeforePause shortages in a row for the
    // same row (about an hour) the whole list is paused and the Telegram message is sent, exactly as before.
    //
    // The streak is memory only (a restart starts counting again; the row's persisted NextExecuteAt survives). A
    // successful send of the row ends its streak.
    public static class RaidListShortageRules
    {
        public const int RetryMinMinutes = 8;
        public const int RetryMaxMinutes = 14;
        public const int MaxConsecutiveBeforePause = 6;

        private static readonly ConcurrentDictionary<int, int> Streaks = new();

        // Counts one more shortage for the row and returns the streak including this one.
        public static int RecordShortage(int entryId) => Streaks.AddOrUpdate(entryId, 1, (_, count) => count + 1);

        public static void Reset(int entryId) => Streaks.TryRemove(entryId, out _);

        public static int Streak(int entryId) => Streaks.TryGetValue(entryId, out var count) ? count : 0;

        public static bool ShouldPauseList(int streak) => streak >= MaxConsecutiveBeforePause;

        public static DateTime NextTry(DateTime now, Random random)
        {
            return now.AddMinutes(random.Next(RetryMinMinutes, RetryMaxMinutes + 1));
        }
    }
}
