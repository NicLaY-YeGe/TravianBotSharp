using System;

namespace MainCore.Commands.Features.UseHeroItem
{
    // 2026-10-29 (log review 2026-10-28 22:38-23:59): the amount taken from the hero inventory is rounded UP to a
    // multiple of 100. Two defects in the old private helper in UseHeroResourceCommand:
    //   1. an amount that already was a multiple of 100 got another 100 on top (400 -> 500);
    //   2. the rounded amount was compared with the inventory BEFORE anything else, so 350 in stock and 321 needed
    //      asked for 400 and failed although the real need could be covered (the log showed ~11% more taken than needed).
    // Pure so it is unit tested. The amount is typed into the game's dialog, so a value that is not a multiple of 100
    // is fine (the storage-capacity clamp after this already produces such values).
    public static class HeroResourceAmountRules
    {
        public static long RoundUpTo100(long amount)
        {
            if (amount <= 0) return 0;
            var remainder = amount % 100;
            return remainder == 0 ? amount : amount + (100 - remainder);
        }

        // need = what is really missing, inStock = what the hero holds of that resource.
        // Rounded up, but never above the stock while the stock still covers the real need.
        // Stock below the real need: the rounded amount is returned unchanged so the caller's validation reports the shortage.
        public static long AmountToUse(long need, long inStock)
        {
            if (need <= 0) return 0;
            var rounded = RoundUpTo100(need);
            if (inStock >= rounded) return rounded;
            if (inStock >= need) return inStock;
            return rounded;
        }

        public static long[] AmountsToUse(long[] need, long[] inStock)
        {
            var result = new long[need.Length];
            for (var i = 0; i < need.Length; i++)
            {
                var stock = i < inStock.Length ? inStock[i] : 0;
                result[i] = AmountToUse(need[i], stock);
            }
            return result;
        }
    }
}
