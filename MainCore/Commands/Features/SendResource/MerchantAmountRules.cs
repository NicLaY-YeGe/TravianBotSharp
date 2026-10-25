namespace MainCore.Commands.Features.SendResource
{
    // 2026-10-25, user request: merchant shipments used to be rounded to multiples of 100; they are
    // now multiples of one merchant's load (750 - a Gaul merchant), so no merchant travels part-empty.
    // Pure so it is unit tested.
    //   - RoundDown: surplus-driven sends (overflow, balance, crop spare). Never sends more than
    //     asked; less than one full load = 0 (the caller skips, it is not worth a merchant).
    //   - RoundUpWithin: need-driven sends (supply for a build, settler or hero revival). Rounds the
    //     wanted amount UP to a full load as long as the source can spare it; otherwise the largest
    //     full load the source can spare (possibly 0). Rounding such a send DOWN could leave a
    //     deficit of a few hundred that is never completed, because the next request would again be
    //     below one load.
    // The value is the Gaul merchant load; other tribes carry 500 / 1000 - change it here if needed.
    public static class MerchantAmountRules
    {
        public const int LoadPerMerchant = 750;

        public static long RoundDown(double wanted)
        {
            if (wanted <= 0) return 0;
            return (long)Math.Floor(wanted / LoadPerMerchant) * LoadPerMerchant;
        }

        public static long RoundUpWithin(double wanted, double ceiling)
        {
            if (wanted <= 0 || ceiling <= 0) return 0;
            var up = (long)Math.Ceiling(wanted / LoadPerMerchant) * LoadPerMerchant;
            if (up <= ceiling) return up;
            return RoundDown(ceiling);
        }
    }
}
