using MainCore.Commands.Features.UpgradeBuilding;

namespace MainCore.Test.Commands.Features.UpgradeBuilding
{
    public class FlexibleBuildRulesTest
    {
        private static readonly long[] NoHero = [0, 0, 0, 0];

        [Theory]
        [InlineData(true, false, 21, 20, true)]    // waits 21 min, threshold 20
        [InlineData(true, false, 20, 20, false)]   // exactly the threshold is not "longer than"
        [InlineData(true, false, 5, 20, false)]
        [InlineData(false, false, 120, 20, false)] // feature off
        [InlineData(true, true, 120, 20, false)]   // Roman queue logic on
        [InlineData(true, false, 0, 20, false)]    // unreadable wait is no reason to reorder
        [InlineData(true, false, 1, 0, true)]      // threshold 0: any known wait
        [InlineData(true, false, 1, -5, true)]     // a negative threshold is treated as 0
        public void ShouldLookBehind_NeedsAKnownWaitLongerThanTheThreshold(bool enabled, bool roman, int waitMinutes, int threshold, bool expected)
        {
            FlexibleBuildRules.ShouldLookBehind(enabled, roman, TimeSpan.FromMinutes(waitMinutes), threshold).ShouldBe(expected);
        }

        [Fact]
        public void Judge_Affordable_WhenTheStockCoversTheCost()
        {
            long[] cost = [100, 200, 300, 50, 2];
            long[] stock = [100, 200, 300, 50];
            FlexibleBuildRules.Judge(cost, stock, 10, 800, 800, NoHero, false).ShouldBe(FlexibleBuildRules.Verdict.Affordable);
        }

        [Fact]
        public void Judge_NotYet_WhenOneResourceIsShort()
        {
            long[] cost = [100, 200, 300, 50, 2];
            long[] stock = [100, 199, 300, 50];
            FlexibleBuildRules.Judge(cost, stock, 10, 800, 800, NoHero, false).ShouldBe(FlexibleBuildRules.Verdict.NotYet);
        }

        [Fact]
        public void Judge_HeroStockCountsOnlyWhenAllowed()
        {
            long[] cost = [100, 200, 300, 50, 2];
            long[] stock = [100, 0, 300, 50];
            long[] hero = [0, 250, 0, 0];

            FlexibleBuildRules.Judge(cost, stock, 10, 800, 800, hero, true).ShouldBe(FlexibleBuildRules.Verdict.Affordable);
            FlexibleBuildRules.Judge(cost, stock, 10, 800, 800, hero, false).ShouldBe(FlexibleBuildRules.Verdict.NotYet);
        }

        [Fact]
        public void Judge_HeroIsAddedToTheStockPerResource()
        {
            long[] cost = [100, 100, 100, 100, 0];
            long[] stock = [60, 60, 60, 60];
            long[] hero = [40, 40, 40, 39];   // crop: 60 + 39 = 99 < 100

            FlexibleBuildRules.Judge(cost, stock, 10, 800, 800, hero, true).ShouldBe(FlexibleBuildRules.Verdict.NotYet);
        }

        [Fact]
        public void Judge_Blocked_WhenTheCostExceedsTheStorage()
        {
            long[] stock = [5000, 5000, 5000, 5000];

            // iron cost above the warehouse
            FlexibleBuildRules.Judge([100, 100, 900, 100, 0], stock, 10, 800, 800, NoHero, false).ShouldBe(FlexibleBuildRules.Verdict.Blocked);
            // crop cost above the granary
            FlexibleBuildRules.Judge([100, 100, 100, 900, 0], stock, 10, 800, 800, NoHero, false).ShouldBe(FlexibleBuildRules.Verdict.Blocked);
        }

        [Fact]
        public void Judge_Blocked_WhenTheFreeCropDoesNotCoverTheUpkeep()
        {
            long[] stock = [1000, 1000, 1000, 1000];
            FlexibleBuildRules.Judge([100, 100, 100, 100, 5], stock, 4, 2000, 2000, NoHero, false).ShouldBe(FlexibleBuildRules.Verdict.Blocked);
            FlexibleBuildRules.Judge([100, 100, 100, 100, 5], stock, 5, 2000, 2000, NoHero, false).ShouldBe(FlexibleBuildRules.Verdict.Affordable);
        }

        [Fact]
        public void Judge_CostWithoutUpkeepEntry_IsStillJudged()
        {
            long[] stock = [1000, 1000, 1000, 1000];
            FlexibleBuildRules.Judge([100, 100, 100, 100], stock, 0, 2000, 2000, NoHero, false).ShouldBe(FlexibleBuildRules.Verdict.Affordable);
        }

        [Fact]
        public void Judge_ShortArrays_AreBlocked()
        {
            FlexibleBuildRules.Judge([1, 2], [1, 2, 3, 4], 0, 100, 100, NoHero, false).ShouldBe(FlexibleBuildRules.Verdict.Blocked);
        }

        [Fact]
        public void PickField_TakesTheLowestAffordableLevel()
        {
            (int Location, int Type, int Level)[] fields =
            [
                (1, 1, 3), (2, 2, 2), (3, 3, 2), (4, 4, 5),
            ];

            // everything affordable: lowest level, lowest location on a tie
            FlexibleBuildRules.PickField(fields, (_, _) => true).ShouldBe(2);
            // clay (type 2) not affordable: iron field at level 2 wins
            FlexibleBuildRules.PickField(fields, (type, _) => type != 2).ShouldBe(3);
        }

        [Fact]
        public void PickField_AsksForTheNextLevel()
        {
            (int Location, int Type, int Level)[] fields = [(1, 1, 3)];
            var asked = new List<(int Type, int Level)>();

            FlexibleBuildRules.PickField(fields, (type, level) =>
            {
                asked.Add((type, level));
                return true;
            });

            asked.Count.ShouldBe(1);
            asked[0].Level.ShouldBe(4);
        }

        [Fact]
        public void PickField_ReturnsMinusOneWhenNothingIsAffordable()
        {
            (int Location, int Type, int Level)[] fields = [(1, 1, 3), (2, 2, 2)];
            FlexibleBuildRules.PickField(fields, (_, _) => false).ShouldBe(-1);
            FlexibleBuildRules.PickField([], (_, _) => true).ShouldBe(-1);
        }
    }
}
