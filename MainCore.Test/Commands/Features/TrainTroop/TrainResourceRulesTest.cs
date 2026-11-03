using MainCore.Commands.Features.TrainTroop;

namespace MainCore.Test.Commands.Features.TrainTroop
{
    public class TrainResourceRulesTest
    {
        // Phalanx-like unit: 100 / 130 / 55 / 30 per unit.
        private static readonly long[] Cost = [100, 130, 55, 30];

        [Fact]
        public void HasCost_NeedsAtLeastOnePositiveValue()
        {
            TrainResourceRules.HasCost(Cost).ShouldBeTrue();
            TrainResourceRules.HasCost([0, 0, 0, 0]).ShouldBeFalse();
            TrainResourceRules.HasCost([1, 2, 3]).ShouldBeFalse();
        }

        [Fact]
        public void NeededAndMissing_ArePerResource()
        {
            var needed = TrainResourceRules.Needed(Cost, 10);
            string.Join(",", needed).ShouldBe("1000,1300,550,300");

            var missing = TrainResourceRules.Missing(needed, [1200, 1000, 550, 100]);
            string.Join(",", missing).ShouldBe("0,300,0,200");
            TrainResourceRules.AnyMissing(missing).ShouldBeTrue();
            TrainResourceRules.AnyMissing(TrainResourceRules.Missing(needed, [2000, 2000, 2000, 2000])).ShouldBeFalse();
        }

        [Fact]
        public void HeroAvailable_KeepsTheReserve()
        {
            var available = TrainResourceRules.HeroAvailable([5000, 800, 0, 300], 500);
            string.Join(",", available).ShouldBe("4500,300,0,0");
            string.Join(",", TrainResourceRules.HeroAvailable([100, 100, 100, 100], -5)).ShouldBe("100,100,100,100");
        }

        [Fact]
        public void UnitsThatFit_IsLimitedByTheScarcestResource()
        {
            // stock 1000/1000/1000/1000 + extra 0: clay allows 7 (910), wood 10, iron 18, crop 33 -> 7.
            TrainResourceRules.UnitsThatFit(20, Cost, [1000, 1000, 1000, 1000], [0, 0, 0, 0]).ShouldBe(7);
            // the hero adds clay 400 -> 1400 / 130 = 10 -> wood (10) now the limit.
            TrainResourceRules.UnitsThatFit(20, Cost, [1000, 1000, 1000, 1000], [0, 400, 0, 0]).ShouldBe(10);
            TrainResourceRules.UnitsThatFit(5, Cost, [1000, 1000, 1000, 1000], [0, 0, 0, 0]).ShouldBe(5);
            TrainResourceRules.UnitsThatFit(5, Cost, [0, 0, 0, 0], [0, 0, 0, 0]).ShouldBe(0);
        }

        [Fact]
        public void NpcTarget_NoDeficit_IsNull()
        {
            TrainResourceRules.NpcTarget([2000, 2000, 2000, 2000], TrainResourceRules.Needed(Cost, 10)).ShouldBeNull();
        }

        [Fact]
        public void NpcTarget_SurplusCannotCoverTheDeficit_IsNull()
        {
            // needs 1300 clay, has 100; the other surpluses add up to far less than 1200.
            TrainResourceRules.NpcTarget([1000, 100, 550, 300], TrainResourceRules.Needed(Cost, 10)).ShouldBeNull();
        }

        [Fact]
        public void NpcTarget_KeepsTheTotalAndCoversTheNeed()
        {
            long[] stock = [3000, 200, 1000, 1000];
            var needed = TrainResourceRules.Needed(Cost, 10); // 1000 1300 550 300
            var target = TrainResourceRules.NpcTarget(stock, needed);

            target.ShouldNotBeNull();
            target![1].ShouldBe(1300);
            target.Sum().ShouldBe(stock.Sum());
            for (var i = 0; i < 4; i++) (target[i] >= needed[i]).ShouldBeTrue();
        }

        [Fact]
        public void NpcTarget_TakesTheDeficitProportionallyFromTheSurplus()
        {
            // deficit 1100 clay; surplus wood 2000 (3000-1000), iron 450 (1000-550), crop 700 (1000-300) = 3150.
            long[] stock = [3000, 200, 1000, 1000];
            var target = TrainResourceRules.NpcTarget(stock, TrainResourceRules.Needed(Cost, 10))!;
            // wood gives the biggest share, and nothing drops below what the batch needs.
            (stock[0] - target[0] > stock[3] - target[3]).ShouldBeTrue();
            (stock[3] - target[3] > stock[2] - target[2]).ShouldBeTrue();
            (target[0] >= 1000 && target[2] >= 550 && target[3] >= 300).ShouldBeTrue();
        }
    }
}
