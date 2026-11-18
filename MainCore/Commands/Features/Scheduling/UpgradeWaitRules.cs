namespace MainCore.Commands.Features.Scheduling
{
    // 2026-10-26 (live log A_1201, 11:38-14:34): a Grain Mill to level 5 was retried 678 times, every ~16 s.
    // The contract page of a building that cannot be afforded shows either a countdown
    // (`.errorMessage span.timer`) or, when crop production is too low, only the red line
    // "Increase crop production." (real capture) - no time at all. UpgradeParser then read 0 s,
    // ExecuteAt became "now" and the task ran again at once. This decides the wait in every case.
    public static class UpgradeWaitRules
    {
        // Crop production too low: nothing can be built until it rises, resources are irrelevant.
        public const int CropBlockedMinMinutes = 120;
        public const int CropBlockedMaxMinutes = 180;

        // No cropland left to upgrade while crop is blocked: park the village's building tasks this long.
        public const int NoCroplandSuspendHours = 3;

        // The page gave no time and no known reason: never "now", never a hot loop.
        public const int UnknownMinMinutes = 10;
        public const int UnknownMaxMinutes = 15;

        // 2026-11-17 (live logs 2026-11-16/17): the build page had no Upgrade button (Cropland slot 9: 4 runs, Smithy slot 21:
        // 1 run) and `GetElement` waited its full 180 s each time, 3-12 minutes of a blocked single queue. The button is now
        // looked for ButtonWaitSeconds only; a missing one makes the task look again later, the wait growing with every
        // miss in a row for the same village (ButtonMissingWait), never "now" (CLAUDE.md rule 16).
        public const int ButtonWaitSeconds = 12;

        public static TimeSpan ButtonMissingWait(int missesInARow, Random random)
        {
            var (min, max) = missesInARow switch
            {
                <= 1 => (4, 8),
                2 => (8, 15),
                3 => (15, 30),
                _ => (30, 45),
            };
            return TimeSpan.FromMinutes(random.Next(min, max + 1));
        }

        public static bool IsCropProductionBlocked(string? message)
        {
            return !string.IsNullOrEmpty(message)
                && message.Contains("crop production", StringComparison.OrdinalIgnoreCase);
        }

        // pageTime: what the page says (0 when it shows no countdown). A readable countdown always wins.
        public static TimeSpan NextWait(TimeSpan pageTime, bool cropBlocked, Random random)
        {
            if (pageTime > TimeSpan.Zero) return pageTime;
            if (cropBlocked) return TimeSpan.FromMinutes(random.Next(CropBlockedMinMinutes, CropBlockedMaxMinutes + 1));
            return TimeSpan.FromMinutes(random.Next(UnknownMinMinutes, UnknownMaxMinutes + 1));
        }
    }
}
