using MainCore.Commands.Features.BuyCrop;
using MainCore.Parsers;

namespace MainCore.Test.Commands.Features.BuyCrop
{
    public class BuyCropRulesTest
    {
        private static readonly DateTime Now = new(2026, 11, 10, 12, 0, 0);

        private static MarketplaceOffer Offer(
            int row, long offered, string wantedType, long wanted, int merchants, TimeSpan duration,
            bool enabled = true, bool resourceMissing = false, bool merchantsMissing = false, string player = "p")
            => new(row, "crop", offered, wantedType, wanted, merchants, duration, enabled, resourceMissing, merchantsMissing, player);

        private static BuyCropRules.Context Ctx(
            long crop = 2000, long pending = 0, long granary = 10000, int target = 60, TimeSpan? tte = null,
            int maxMinutes = 0, int merchants = 10, double wood = 0.5, double clay = 0.5, double iron = 0.5)
            => new(crop, pending, granary, target, tte, maxMinutes, merchants, wood, clay, iron);

        // ---- trigger / need / starvation ETA ----

        [Theory]
        [InlineData(1900, 0, 10000, 20, true)]   // 19 % < 20 %
        [InlineData(2000, 0, 10000, 20, false)]  // exactly at the trigger is not "below"
        [InlineData(1000, 1500, 10000, 20, false)] // crop on its way counts as stock
        [InlineData(500, 0, 10000, 0, false)]    // 0 % = off
        [InlineData(500, 0, 0, 20, false)]       // no granary data
        public void ShouldBuy(long crop, long pending, long granary, int threshold, bool expected)
        {
            BuyCropRules.ShouldBuy(crop, pending, granary, threshold).ShouldBe(expected);
        }

        [Fact]
        public void Need_IsTheGapToTheTarget_NeverNegative()
        {
            BuyCropRules.Need(2000, 0, 10000, 60).ShouldBe(4000);
            BuyCropRules.Need(2000, 1500, 10000, 60).ShouldBe(2500);
            BuyCropRules.Need(7000, 0, 10000, 60).ShouldBe(0);
        }

        [Fact]
        public void TimeToEmpty_OnlyForANegativeBalance()
        {
            BuyCropRules.TimeToEmpty(1000, 0).ShouldBeNull();
            BuyCropRules.TimeToEmpty(1000, 250).ShouldBeNull();
            BuyCropRules.TimeToEmpty(1000, -500).ShouldBe(TimeSpan.FromHours(2));
            BuyCropRules.TimeToEmpty(0, -500).ShouldBe(TimeSpan.Zero);
        }

        [Fact]
        public void ArrivesInTime_NeedsTheMarginBeforeTheStoreIsEmpty()
        {
            var tte = TimeSpan.FromHours(2);
            BuyCropRules.ArrivesInTime(TimeSpan.FromHours(1), tte).ShouldBeTrue();
            BuyCropRules.ArrivesInTime(TimeSpan.FromMinutes(119), tte).ShouldBeFalse(); // inside the 2 min margin
            BuyCropRules.ArrivesInTime(TimeSpan.FromHours(3), tte).ShouldBeFalse();
            BuyCropRules.ArrivesInTime(TimeSpan.FromHours(30), null).ShouldBeTrue();
        }

        // ---- offer selection ----

        [Fact]
        public void Select_PrefersTheShortestTrip()
        {
            var offers = new[]
            {
                Offer(1, 2400, "iron", 2400, 3, TimeSpan.FromHours(6)),
                Offer(2, 3900, "clay", 3500, 4, TimeSpan.FromMinutes(95)),
            };
            var selection = BuyCropRules.Select(offers, Ctx());
            selection.Chosen!.RowIndex.ShouldBe(2);
            selection.Late.ShouldBeFalse();
        }

        [Fact]
        public void Select_NeverPaysMoreThanItGets_AndSkipsGreyedOutOffers()
        {
            var offers = new[]
            {
                Offer(1, 1000, "clay", 1200, 2, TimeSpan.FromMinutes(10)),                       // worse than 1:1
                Offer(2, 2000, "iron", 2000, 2, TimeSpan.FromMinutes(20), enabled: false, resourceMissing: true),
                Offer(3, 2400, "iron", 2400, 3, TimeSpan.FromMinutes(30), enabled: false, merchantsMissing: true),
                Offer(4, 1200, "clay", 1200, 2, TimeSpan.FromMinutes(40)),
            };
            var selection = BuyCropRules.Select(offers, Ctx());
            selection.Chosen!.RowIndex.ShouldBe(4);
            selection.Rejected["worse than 1:1"].ShouldBe(1);
            selection.Rejected["not enough resources to pay"].ShouldBe(1);
            selection.Rejected["not enough free merchants"].ShouldBe(1);
        }

        [Fact]
        public void Select_HonoursTheTripLimit_ZeroMeansNoLimit()
        {
            var offers = new[] { Offer(1, 2400, "iron", 2400, 3, TimeSpan.FromHours(6)) };
            BuyCropRules.Select(offers, Ctx(maxMinutes: 60)).Chosen.ShouldBeNull();
            BuyCropRules.Select(offers, Ctx(maxMinutes: 60)).Rejected["further away than the limit"].ShouldBe(1);
            BuyCropRules.Select(offers, Ctx(maxMinutes: 0)).Chosen.ShouldNotBeNull();
            BuyCropRules.Select(offers, Ctx(maxMinutes: 400)).Chosen.ShouldNotBeNull();
        }

        [Fact]
        public void Select_NeverOverflowsTheGranary_CropOnItsWayCounts()
        {
            var offers = new[] { Offer(1, 5460, "clay", 5460, 6, TimeSpan.FromMinutes(23)) };
            BuyCropRules.Select(offers, Ctx(crop: 5000, granary: 10000)).Chosen.ShouldBeNull();           // 5000 + 5460 > 10000
            BuyCropRules.Select(offers, Ctx(crop: 2000, granary: 10000)).Chosen.ShouldNotBeNull();        // 7460
            BuyCropRules.Select(offers, Ctx(crop: 2000, pending: 3000, granary: 10000)).Chosen.ShouldBeNull(); // 10460
        }

        [Fact]
        public void Select_NeedsEnoughFreeMerchants()
        {
            var offers = new[] { Offer(1, 2400, "iron", 2400, 3, TimeSpan.FromMinutes(30)) };
            BuyCropRules.Select(offers, Ctx(merchants: 2)).Chosen.ShouldBeNull();
            BuyCropRules.Select(offers, Ctx(merchants: 3)).Chosen.ShouldNotBeNull();
        }

        [Fact]
        public void Select_AmongEquallyFarOffers_PaysWithTheFullestStore()
        {
            var d = TimeSpan.FromMinutes(30);
            var offers = new[]
            {
                Offer(1, 1200, "clay", 1200, 2, d),
                Offer(2, 1200, "iron", 1200, 2, d.Add(TimeSpan.FromMinutes(3))),   // within the 5 min tie window
                Offer(3, 1200, "wood", 1200, 2, d.Add(TimeSpan.FromMinutes(30))),  // too much further away to be in the tie
            };
            var ctx = Ctx(wood: 0.99, clay: 0.40, iron: 0.90);
            BuyCropRules.Select(offers, ctx).Chosen!.RowIndex.ShouldBe(2);
        }

        [Fact]
        public void Select_AmongEqualOffers_TakesTheLargestThatFitsTheNeed_ElseTheSmallest()
        {
            var d = TimeSpan.FromMinutes(30);
            var offers = new[]
            {
                Offer(1, 1200, "clay", 1200, 2, d),
                Offer(2, 2400, "clay", 2400, 3, d),
                Offer(3, 5460, "clay", 5460, 6, d),
            };
            // need = 60 % of 10000 - 2000 = 4000: 2400 is the largest that fits
            BuyCropRules.Select(offers, Ctx()).Chosen!.RowIndex.ShouldBe(2);
            // need = 60 % of 10000 - 5500 = 500: nothing fits, the smallest overshoot wins
            BuyCropRules.Select(offers, Ctx(crop: 5500)).Chosen!.RowIndex.ShouldBe(1);
        }

        [Fact]
        public void Select_DropsOffersThatArriveAfterTheCropRunsOut_WhenAnotherOneIsInTime()
        {
            var tte = TimeSpan.FromHours(2);
            var offers = new[]
            {
                Offer(1, 3900, "clay", 3500, 4, TimeSpan.FromMinutes(95)),
                Offer(2, 2400, "iron", 2400, 3, TimeSpan.FromHours(6)),
            };
            var selection = BuyCropRules.Select(offers, Ctx(tte: tte));
            selection.Chosen!.RowIndex.ShouldBe(1);
            selection.Late.ShouldBeFalse();
        }

        [Fact]
        public void Select_WhenNothingArrivesInTime_TakesTheNearestAnywayAndSaysSo()
        {
            var offers = new[]
            {
                Offer(1, 2400, "iron", 2400, 3, TimeSpan.FromHours(6)),
                Offer(2, 3900, "clay", 3500, 4, TimeSpan.FromHours(3)),
            };
            var selection = BuyCropRules.Select(offers, Ctx(tte: TimeSpan.FromMinutes(20)));
            selection.Chosen!.RowIndex.ShouldBe(2);
            selection.Late.ShouldBeTrue();

            // an already empty store: every offer is late, the nearest still wins
            var empty = BuyCropRules.Select(offers, Ctx(crop: 0, tte: TimeSpan.Zero));
            empty.Chosen!.RowIndex.ShouldBe(2);
            empty.Late.ShouldBeTrue();
        }

        [Fact]
        public void Select_IgnoresOffersThatDoNotGiveCrop()
        {
            var offers = new[] { new MarketplaceOffer(1, "clay", 2000, "crop", 2000, 2, TimeSpan.FromMinutes(5), true, false, false, "p") };
            var selection = BuyCropRules.Select(offers, Ctx());
            selection.Chosen.ShouldBeNull();
            selection.Rejected["not crop"].ShouldBe(1);
        }

        // ---- scheduling helpers ----

        [Fact]
        public void NextCheck_StaysInsideTheRange()
        {
            for (var i = 0; i < 50; i++)
            {
                var next = BuyCropRules.NextCheck(Now, BuyCropRules.NothingBoughtMinMinutes, BuyCropRules.NothingBoughtMaxMinutes, new Random(i));
                var minutes = (next - Now).TotalMinutes;
                (minutes >= BuyCropRules.NothingBoughtMinMinutes).ShouldBeTrue();
                (minutes <= BuyCropRules.NothingBoughtMaxMinutes).ShouldBeTrue();
            }
        }

        [Fact]
        public void Arrival_IsTheTripPlusAMinute()
        {
            BuyCropRules.Arrival(Now, TimeSpan.FromMinutes(30)).ShouldBe(Now.AddMinutes(31));
        }
    }
}
