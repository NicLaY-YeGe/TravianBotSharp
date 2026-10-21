using MainCore.Commands.Features.DefendDonate;

namespace MainCore.Test.Commands.Features.DefendDonate
{
    public class DefendResourceTargetRulesTest
    {
        private static DefendResources Res(long w, long c, long i, long cr) => new(w, c, i, cr);

        // Stock + capacities; room = capacity - stock.
        private static DefendTargetCandidate Village(int id, long stock, long warehouse, long granary, bool underAttack = false)
            => new(id, Res(stock, stock, stock, stock), warehouse, granary, underAttack);

        [Fact]
        public void ScaleToCapacity_FitsAlready_Unchanged()
        {
            var scaled = DefendResourceTargetRules.ScaleToCapacity(Res(100, 200, 300, 400), 1000);
            scaled.ShouldBe(Res(100, 200, 300, 400));
        }

        [Fact]
        public void ScaleToCapacity_TooMuch_ScalesProportionally()
        {
            var scaled = DefendResourceTargetRules.ScaleToCapacity(Res(1000, 1000, 1000, 1000), 2000);
            scaled.ShouldBe(Res(500, 500, 500, 500));
        }

        [Fact]
        public void ScaleToCapacity_NoCapacity_AllZero()
        {
            var scaled = DefendResourceTargetRules.ScaleToCapacity(Res(10, 10, 10, 10), 0);
            scaled.Total.ShouldBe(0L);
        }

        [Fact]
        public void FitFraction_FullRoom_IsOne()
        {
            var fit = DefendResourceTargetRules.FitFraction(Village(1, 0, 10000, 10000), Res(1000, 1000, 1000, 1000));
            fit.ShouldBe(1.0);
        }

        [Fact]
        public void FitFraction_HalfRoom_IsHalf()
        {
            // 500 room per resource, 1000 sent each -> half fits.
            var fit = DefendResourceTargetRules.FitFraction(Village(1, 9500, 10000, 10000), Res(1000, 1000, 1000, 1000));
            fit.ShouldBe(0.5);
        }

        [Fact]
        public void FitFraction_FullStorage_IsZero()
        {
            var fit = DefendResourceTargetRules.FitFraction(Village(1, 10000, 10000, 10000), Res(1000, 1000, 1000, 1000));
            fit.ShouldBe(0.0);
        }

        [Fact]
        public void FitFraction_UsesGranaryForCrop()
        {
            // Warehouse has room, granary is full: only wood/clay/iron fit.
            var candidate = new DefendTargetCandidate(1, Res(0, 0, 0, 5000), 10000, 5000, false);
            var fit = DefendResourceTargetRules.FitFraction(candidate, Res(1000, 1000, 1000, 1000));
            fit.ShouldBe(0.75);
        }

        [Fact]
        public void ChooseTarget_NoCandidates_Null()
        {
            DefendResourceTargetRules.ChooseTarget([], Res(1, 1, 1, 1), new Random(1)).ShouldBeNull();
        }

        [Fact]
        public void ChooseTarget_PrefersVillageThatFitsEverything()
        {
            var candidates = new List<DefendTargetCandidate>
            {
                Village(1, 10000, 10000, 10000),   // full
                Village(2, 9500, 10000, 10000),    // half
                Village(3, 0, 10000, 10000),       // fits all
            };

            for (var seed = 0; seed < 50; seed++)
            {
                DefendResourceTargetRules.ChooseTarget(candidates, Res(1000, 1000, 1000, 1000), new Random(seed)).ShouldBe(3);
            }
        }

        [Fact]
        public void ChooseTarget_SeveralFit_PicksAmongThemOnly()
        {
            var candidates = new List<DefendTargetCandidate>
            {
                Village(1, 0, 10000, 10000),
                Village(2, 0, 10000, 10000),
                Village(3, 10000, 10000, 10000),
            };

            var seen = new HashSet<int>();
            for (var seed = 0; seed < 100; seed++)
            {
                var id = DefendResourceTargetRules.ChooseTarget(candidates, Res(1000, 1000, 1000, 1000), new Random(seed));
                seen.Add(id!.Value);
            }

            seen.Contains(3).ShouldBeFalse();
            (seen.Count == 2).ShouldBeTrue();
        }

        [Fact]
        public void ChooseTarget_NoneFits_PicksBestFit()
        {
            var candidates = new List<DefendTargetCandidate>
            {
                Village(1, 10000, 10000, 10000),   // 0
                Village(2, 9500, 10000, 10000),    // 0.5
                Village(3, 9900, 10000, 10000),    // 0.1
            };

            DefendResourceTargetRules.ChooseTarget(candidates, Res(1000, 1000, 1000, 1000), new Random(1)).ShouldBe(2);
        }

        [Fact]
        public void ChooseTarget_SkipsVillagesUnderAttack()
        {
            var candidates = new List<DefendTargetCandidate>
            {
                Village(1, 0, 10000, 10000, underAttack: true),
                Village(2, 9000, 10000, 10000),
            };

            // Village 1 has all the room but is under attack; 2 is the only eligible one.
            DefendResourceTargetRules.ChooseTarget(candidates, Res(1000, 1000, 1000, 1000), new Random(1)).ShouldBe(2);
        }

        [Fact]
        public void ChooseTarget_AllUnderAttack_StillPicksOne()
        {
            var candidates = new List<DefendTargetCandidate>
            {
                Village(1, 0, 10000, 10000, underAttack: true),
                Village(2, 9000, 10000, 10000, underAttack: true),
            };

            DefendResourceTargetRules.ChooseTarget(candidates, Res(1000, 1000, 1000, 1000), new Random(1)).ShouldBe(1);
        }
    }
}
