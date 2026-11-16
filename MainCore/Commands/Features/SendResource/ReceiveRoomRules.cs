namespace MainCore.Commands.Features.SendResource
{
    // 2026-11-15 (live log 2026-11-15 00:01-11:35, ~20 shipments of 20-33k crop between two villages):
    // Balance resources (village A -> B, "most free room") and Overflow to hammer (B -> hammer A) fed each other.
    // Both tasks counted the receiver's room as capacity - stock, so a shipment filled B to ~100 %, B was then above
    // its OWN outgoing threshold (Overflow to hammer percent / Auto balance overflow percent) and shipped the load
    // straight back; the same happened in the other direction. A receiver must only be filled up to just BELOW the
    // percent at which it would start sending resources away itself. Pure, so it is unit tested.
    public static class ReceiveRoomRules
    {
        // The lowest percent at which the receiver would itself start sending this resource away.
        // A disabled feature does not limit; a percent outside 1..99 is ignored as "no limit", except 0 or less,
        // which means "always above the threshold" and so leaves no room at all.
        public static int CeilingPercent(bool balanceEnabled, int balanceOverflowPercent, bool overflowToHammerEnabled, int overflowToHammerPercent)
        {
            var ceiling = 100;
            if (balanceEnabled) ceiling = Math.Min(ceiling, Clamp(balanceOverflowPercent));
            if (overflowToHammerEnabled) ceiling = Math.Min(ceiling, Clamp(overflowToHammerPercent));
            return ceiling;
        }

        private static int Clamp(int percent) => percent >= 100 ? 100 : Math.Max(percent, 0);

        // How much of one resource the receiver can take without reaching its own outgoing threshold.
        // The sending tasks fire at "stock * 100 / capacity >= percent", so the highest stock that stays quiet is the
        // largest whole number strictly below capacity * percent / 100.
        public static long Room(long capacity, long current, int ceilingPercent)
        {
            if (capacity <= 0) return 0;
            if (ceilingPercent >= 100) return Math.Max(0, capacity - current);
            if (ceilingPercent <= 0) return 0;

            var thresholdCeil = (capacity * ceilingPercent + 99) / 100;
            var highestQuietStock = thresholdCeil - 1;
            return Math.Max(0, highestQuietStock - current);
        }
    }
}
