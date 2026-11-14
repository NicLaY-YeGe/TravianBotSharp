using MainCore.Commands.Features.ShareTrain;

namespace MainCore.Test.Commands.Features.ShareTrain
{
    public class ShareTrainRulesTest
    {
        private static readonly IReadOnlyDictionary<int, long> NoBudgets = new Dictionary<int, long>();

        private static long Total(long[] cost, int units) => cost.Sum() * units;

        [Fact]
        public void Surplus_IsStockAboveTheFloor()
        {
            string.Join("/", ShareTrainRules.Surplus([1000, 500, 200, 900], [400, 600, 200, 100])).ShouldBe("600/0/0/800");
        }

        [Fact]
        public void ShouldRun_NeedsTheSurplusToReachThePercentOfTheStorage()
        {
            // capacity = 3 x 1000 + 1000 = 4000; 20 % = 800
            ShareTrainRules.ShouldRun([300, 300, 100, 99], 1000, 1000, 20).ShouldBeFalse();
            ShareTrainRules.ShouldRun([300, 300, 100, 100], 1000, 1000, 20).ShouldBeTrue();
        }

        [Fact]
        public void ShouldRun_NothingToSpendOrUnknownStorageNeverRuns()
        {
            ShareTrainRules.ShouldRun([0, 0, 0, 0], 1000, 1000, 0).ShouldBeFalse();
            ShareTrainRules.ShouldRun([500, 500, 500, 500], 0, 1000, 10).ShouldBeFalse();
            ShareTrainRules.ShouldRun([500, 500, 500, 500], 1000, 0, 10).ShouldBeFalse();
        }

        [Fact]
        public void ShouldRun_PercentZeroRunsWheneverThereIsSurplus()
        {
            ShareTrainRules.ShouldRun([1, 0, 0, 0], 1000, 1000, 0).ShouldBeTrue();
        }

        [Fact]
        public void GroupBudget_IsTheRoomLeftUnderTheCeiling()
        {
            ShareTrainRules.GroupBudgetSeconds(0, 5000).ShouldBe(ShareTrainRules.NoBudget);
            ShareTrainRules.GroupBudgetSeconds(60, 600).ShouldBe(3000);
            ShareTrainRules.GroupBudgetSeconds(60, 99999).ShouldBe(0);
        }

        [Fact]
        public void Allocate_SplitsEqualUnitsByTheWeights()
        {
            // same unit cost, weights 3:1 -> 3/4 and 1/4 of the units
            long[] cost = [100, 100, 100, 100];
            var candidates = new List<ShareCandidate>
            {
                new(0, 0, 3, cost, 0),
                new(1, 0, 1, cost, 0),
            };
            var units = ShareTrainRules.Allocate(candidates, NoBudgets, [4000, 4000, 4000, 4000]);

            units[0].ShouldBe(30);
            units[1].ShouldBe(10);
        }

        [Fact]
        public void Allocate_NeverSpendsMoreThanThePool()
        {
            long[] cheap = [60, 50, 40, 10];
            long[] dear = [700, 600, 500, 100];
            var candidates = new List<ShareCandidate>
            {
                new(0, 0, 5, cheap, 0),
                new(1, 1, 4, dear, 0),
                new(2, 1, 2, [150, 120, 90, 20], 0),
            };
            long[] pool = [12000, 9000, 7000, 3000];
            var units = ShareTrainRules.Allocate(candidates, NoBudgets, pool);

            for (var r = 0; r < 4; r++)
            {
                var spent = units[0] * cheap[r] + units[1] * dear[r] + units[2] * candidates[2].UnitCost[r];
                (spent <= pool[r]).ShouldBeTrue();
            }
        }

        [Fact]
        public void Allocate_WeightZeroIsNeverTrained()
        {
            long[] cost = [10, 10, 10, 10];
            var candidates = new List<ShareCandidate> { new(0, 0, 0, cost, 0), new(1, 0, 2, cost, 0) };
            var units = ShareTrainRules.Allocate(candidates, NoBudgets, [1000, 1000, 1000, 1000]);

            units[0].ShouldBe(0);
            units[1].ShouldBe(100);
        }

        [Fact]
        public void Allocate_WhatOneTroopCannotAffordFlowsToTheOthers()
        {
            // troop 0 has the big weight but needs crop the pool does not have -> troop 1 takes everything
            long[] needsCrop = [10, 10, 10, 50];
            long[] noCrop = [10, 10, 10, 0];
            var candidates = new List<ShareCandidate> { new(0, 0, 9, needsCrop, 0), new(1, 0, 1, noCrop, 0) };
            var units = ShareTrainRules.Allocate(candidates, NoBudgets, [1000, 1000, 1000, 0]);

            units[0].ShouldBe(0);
            units[1].ShouldBe(100);
        }

        [Fact]
        public void Allocate_ALeftoverGoesOnByTheSameWeights()
        {
            // troop 0 can only use wood/clay, troop 1 only iron/crop: each gets its own resources up to the pool
            var candidates = new List<ShareCandidate>
            {
                new(0, 0, 1, [10, 10, 0, 0], 0),
                new(1, 0, 1, [0, 0, 10, 10], 0),
            };
            var units = ShareTrainRules.Allocate(candidates, NoBudgets, [1000, 1000, 1000, 1000]);

            units[0].ShouldBe(100);
            units[1].ShouldBe(100);
        }

        [Fact]
        public void Allocate_TheQueueCeilingLimitsABuildingAndTheRestGoesElsewhere()
        {
            long[] cost = [10, 10, 10, 10];
            // building 0 may take 600 s = 10 units of 60 s; building 1 has no ceiling
            var candidates = new List<ShareCandidate>
            {
                new(0, 0, 1, cost, 60),
                new(1, 1, 1, cost, 60),
            };
            var budgets = new Dictionary<int, long> { [0] = 600, [1] = ShareTrainRules.NoBudget };
            var units = ShareTrainRules.Allocate(candidates, budgets, [1000, 1000, 1000, 1000]);

            units[0].ShouldBe(10);
            units[1].ShouldBe(90);
        }

        [Fact]
        public void Allocate_TroopsOfOneBuildingShareItsCeiling()
        {
            long[] cost = [10, 10, 10, 10];
            var candidates = new List<ShareCandidate>
            {
                new(0, 0, 1, cost, 60),
                new(1, 0, 1, cost, 120),
            };
            var budgets = new Dictionary<int, long> { [0] = 600 };
            var units = ShareTrainRules.Allocate(candidates, budgets, [100000, 100000, 100000, 100000]);

            (units[0] * 60L + units[1] * 120L <= 600).ShouldBeTrue();
        }

        [Fact]
        public void Allocate_AFullQueueTrainsNothing()
        {
            long[] cost = [10, 10, 10, 10];
            var candidates = new List<ShareCandidate> { new(0, 0, 1, cost, 60) };
            var units = ShareTrainRules.Allocate(candidates, new Dictionary<int, long> { [0] = 0 }, [1000, 1000, 1000, 1000]);

            units[0].ShouldBe(0);
        }

        [Fact]
        public void Allocate_AnUnknownUnitTimeIsNotLimitedByTheCeiling()
        {
            long[] cost = [10, 10, 10, 10];
            var candidates = new List<ShareCandidate> { new(0, 0, 1, cost, 0) };
            var units = ShareTrainRules.Allocate(candidates, new Dictionary<int, long> { [0] = 0 }, [1000, 1000, 1000, 1000]);

            units[0].ShouldBe(100);
        }

        [Fact]
        public void Allocate_ACrumbThatNoShareCoversGoesToTheHeaviestTroop()
        {
            // pool pays exactly ONE unit of 100; the 1:9 shares are 10 and 90 each - neither covers a unit
            long[] cost = [100, 100, 100, 100];
            var candidates = new List<ShareCandidate> { new(0, 0, 1, cost, 0), new(1, 0, 9, cost, 0) };
            var units = ShareTrainRules.Allocate(candidates, NoBudgets, [100, 100, 100, 100]);

            units[1].ShouldBe(1);
            units[0].ShouldBe(0);
        }

        [Fact]
        public void Allocate_UnreadableCostsAndEmptyPoolDoNothing()
        {
            var candidates = new List<ShareCandidate> { new(0, 0, 5, [0, 0, 0, 0], 0), new(1, 0, 5, [10, 10, 10, 10], 0) };
            var units = ShareTrainRules.Allocate(candidates, NoBudgets, [0, 0, 0, 0]);

            units[0].ShouldBe(0);
            units[1].ShouldBe(0);
            ShareTrainRules.HasCost([0, 0, 0, 0]).ShouldBeFalse();
            ShareTrainRules.HasCost([0, 0, 1, 0]).ShouldBeTrue();
        }

        [Fact]
        public void Allocate_SpendsAlmostEverythingTheTroopsCanUse()
        {
            long[] cost = [100, 100, 100, 100];
            var candidates = new List<ShareCandidate> { new(0, 0, 5, cost, 0), new(1, 0, 3, cost, 0), new(2, 0, 2, cost, 0) };
            var units = ShareTrainRules.Allocate(candidates, NoBudgets, [10050, 10050, 10050, 10050]);

            (units[0] + units[1] + units[2]).ShouldBe(100);
            (units[0] > units[1] && units[1] > units[2]).ShouldBeTrue();
        }

        [Fact]
        public void NextCheck_IsShorterAfterAVisitThatTrained()
        {
            var now = new DateTime(2026, 11, 13, 12, 0, 0);
            var trained = ShareTrainRules.NextCheck(now, true, new Random(1));
            var nothing = ShareTrainRules.NextCheck(now, false, new Random(1));

            (trained >= now.AddMinutes(ShareTrainRules.TrainedMinMinutes) && trained <= now.AddMinutes(ShareTrainRules.TrainedMaxMinutes)).ShouldBeTrue();
            (nothing >= now.AddMinutes(ShareTrainRules.NothingMinMinutes) && nothing <= now.AddMinutes(ShareTrainRules.NothingMaxMinutes)).ShouldBeTrue();
        }
    }
}
