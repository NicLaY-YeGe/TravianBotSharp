using MainCore.Parsers;

namespace MainCore.Commands.Features.BuyCrop
{
    // 2026-11-10 (user request: "tahil ihtiyacimi Buy ile cozmek istiyorum", purpose = keep the troops from starving,
    // even if only temporarily). Pure decisions for BuyCropTask / BuyCropCommand, unit tested.
    //
    // User decisions: independent of the NPC exchange; the construction reserve is ignored (crop has priority); the
    // longest trip is a setting (0 = no limit); starvation ETA from Storage.FreeCrop; several offers per run; the
    // offer's wanted resource is chosen by how full that store is; crop already on its way is remembered
    // (BuyCropMemory).
    public static class BuyCropRules
    {
        public const int MaxOffersPerRun = 3;
        public const int MaxPages = 3;

        // Offers whose trip is within this many minutes of the shortest one count as "equally far": among them the
        // fullest paying store wins.
        public const int DurationTieMinutes = 5;

        // An offer must arrive this much before the crop runs out to count as "in time".
        public const int ArrivalMarginMinutes = 2;

        // Next look when nothing could be bought / something was bought / the run failed.
        public const int NothingBoughtMinMinutes = 15;
        public const int NothingBoughtMaxMinutes = 25;
        public const int BoughtMinMinutes = 8;
        public const int BoughtMaxMinutes = 12;
        public const int FailedMinMinutes = 10;
        public const int FailedMaxMinutes = 20;

        // Crop (and what is already on its way) is below the trigger. 0 % = the trigger is off.
        public static bool ShouldBuy(long crop, long pending, long granary, int thresholdPercent)
        {
            if (thresholdPercent <= 0 || granary <= 0) return false;
            return EffectivePercent(crop, pending, granary) < thresholdPercent;
        }

        public static float EffectivePercent(long crop, long pending, long granary)
        {
            if (granary <= 0) return 100f;
            return (Math.Max(crop, 0) + Math.Max(pending, 0)) * 100f / granary;
        }

        // How much crop is still missing to the target (never negative).
        public static long Need(long crop, long pending, long granary, int targetPercent)
        {
            var target = granary * Math.Clamp(targetPercent, 0, 100) / 100;
            return Math.Max(0, target - Math.Max(crop, 0) - Math.Max(pending, 0));
        }

        // Time until the crop store is empty at the current balance; null = the balance is not negative.
        public static TimeSpan? TimeToEmpty(long crop, long freeCrop)
        {
            if (freeCrop >= 0) return null;
            return TimeSpan.FromHours((double)Math.Max(crop, 0) / -freeCrop);
        }

        public static bool ArrivesInTime(TimeSpan duration, TimeSpan? timeToEmpty)
        {
            if (timeToEmpty is null) return true;
            return duration + TimeSpan.FromMinutes(ArrivalMarginMinutes) < timeToEmpty.Value;
        }

        public sealed record Context(
            long Crop,
            long Pending,
            long Granary,
            int TargetPercent,
            TimeSpan? TimeToEmpty,
            int MaxMinutes,
            int FreeMerchants,
            double WoodFill,
            double ClayFill,
            double IronFill);

        // Chosen = the offer to accept (null = none); Late = no offer arrives before the crop runs out, the nearest
        // one was chosen anyway; Rejected = why the other rows were left out (for the log).
        public sealed record Selection(MarketplaceOffer? Chosen, bool Late, IReadOnlyDictionary<string, int> Rejected);

        public static Selection Select(IReadOnlyList<MarketplaceOffer> offers, Context ctx)
        {
            var rejected = new Dictionary<string, int>();
            void Reject(string reason) => rejected[reason] = rejected.GetValueOrDefault(reason) + 1;

            var candidates = new List<MarketplaceOffer>();
            foreach (var offer in offers)
            {
                if (offer.OfferedType != "crop") { Reject("not crop"); continue; }
                // Never pay more than we get: the page filter "1:1 or better" is a convenience, this is the rule.
                if (offer.OfferedAmount <= 0 || offer.OfferedAmount < offer.WantedAmount) { Reject("worse than 1:1"); continue; }
                if (!offer.Enabled)
                {
                    Reject(offer.MerchantsMissing ? "not enough free merchants" : offer.ResourceMissing ? "not enough resources to pay" : "accept button disabled");
                    continue;
                }
                if (offer.Merchants > ctx.FreeMerchants) { Reject("not enough free merchants"); continue; }
                if (ctx.MaxMinutes > 0 && offer.Duration > TimeSpan.FromMinutes(ctx.MaxMinutes)) { Reject("further away than the limit"); continue; }
                if (ctx.Crop + ctx.Pending + offer.OfferedAmount > ctx.Granary) { Reject("would overflow the granary"); continue; }
                candidates.Add(offer);
            }

            if (candidates.Count == 0) return new Selection(null, false, rejected);

            var pool = candidates.Where(x => ArrivesInTime(x.Duration, ctx.TimeToEmpty)).ToList();
            var late = false;
            if (pool.Count == 0)
            {
                // Nothing arrives before the store is empty (or it is empty already): a late delivery still ends the
                // starving sooner than none, so the nearest offers stay in the race.
                pool = candidates;
                late = true;
            }

            var shortest = pool.Min(x => x.Duration);
            var tie = pool.Where(x => x.Duration <= shortest + TimeSpan.FromMinutes(DurationTieMinutes)).ToList();

            var need = Need(ctx.Crop, ctx.Pending, ctx.Granary, ctx.TargetPercent);
            var chosen = tie
                .OrderByDescending(x => Math.Round(Fill(x.WantedType, ctx) * 100))
                .ThenBy(x => x.OfferedAmount <= need ? 0 : 1)
                .ThenBy(x => x.OfferedAmount <= need ? -x.OfferedAmount : x.OfferedAmount)
                .ThenBy(x => x.Duration)
                .First();

            return new Selection(chosen, late, rejected);
        }

        // How full the store of the resource we would pay with is (0..1).
        public static double Fill(string resourceType, Context ctx) => resourceType switch
        {
            "wood" => ctx.WoodFill,
            "clay" => ctx.ClayFill,
            "iron" => ctx.IronFill,
            _ => 0,
        };

        public static DateTime NextCheck(DateTime now, int minMinutes, int maxMinutes, Random random)
        {
            return now.AddMinutes(random.Next(minMinutes, maxMinutes + 1));
        }

        // When the bought crop is expected to be in the granary (the offer's trip time plus a minute).
        public static DateTime Arrival(DateTime now, TimeSpan duration) => now + duration + TimeSpan.FromMinutes(1);

        public static string RejectedText(IReadOnlyDictionary<string, int> rejected)
        {
            if (rejected.Count == 0) return "none";
            return string.Join(", ", rejected.OrderByDescending(x => x.Value).Select(x => $"{x.Key} x{x.Value}"));
        }
    }
}
