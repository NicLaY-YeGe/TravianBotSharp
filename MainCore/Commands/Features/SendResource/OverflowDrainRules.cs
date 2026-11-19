namespace MainCore.Commands.Features.SendResource
{
    // 2026-11-17 (live log 2026-11-17 00:04-10:31): Overflow to hammer sent only what stood ABOVE the overflow percent, so
    // the sender ended every shipment exactly at the threshold and production lifted it over again within ~19 minutes:
    // 16 Marketplace visits with 750-6750 per resource in 5 h (village 33411 -> hammer 22459). Now the task still STARTS at
    // the overflow percent but drains the village down to DrainMarginPercent points BELOW it, so one shipment buys hours
    // instead of minutes (hysteresis). User decision 2026-11-17: margin 15 points.
    public static class OverflowDrainRules
    {
        public const int DrainMarginPercent = 15;

        // Percent the village is drained down to. Never below half of the threshold, so a low threshold (e.g. 20 %) is not
        // emptied to nothing; a threshold of 0 or less drains to 0.
        public static int DrainTargetPercent(int overflowPercent)
        {
            if (overflowPercent <= 0) return 0;
            return Math.Max(overflowPercent - DrainMarginPercent, overflowPercent / 2);
        }

        // How much of one resource the village may send: its stock above the drain target (0 when below it).
        public static long SendableAmount(long capacity, long current, int overflowPercent)
        {
            if (capacity <= 0) return 0;
            var targetAmount = capacity * DrainTargetPercent(overflowPercent) / 100;
            return Math.Max(0, current - targetAmount);
        }
    }
}
