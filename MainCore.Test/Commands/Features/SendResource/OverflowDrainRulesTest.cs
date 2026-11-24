using MainCore.Commands.Features.SendResource;

namespace MainCore.Test.Commands.Features.SendResource
{
    public class OverflowDrainRulesTest
    {
        [Theory]
        [InlineData(90, 75)]
        [InlineData(80, 65)]
        [InlineData(30, 15)]
        [InlineData(20, 10)]   // never below half of the threshold
        [InlineData(10, 5)]
        [InlineData(1, 0)]
        [InlineData(0, 0)]
        [InlineData(-5, 0)]
        public void DrainTargetPercent_IsFifteenPointsBelowButNotBelowHalf(int threshold, int expected)
        {
            OverflowDrainRules.DrainTargetPercent(threshold).ShouldBe(expected);
        }

        [Fact]
        public void SendableAmount_IsTheStockAboveTheDrainTarget()
        {
            // warehouse 40,000, threshold 90 % -> drain to 75 % = 30,000
            OverflowDrainRules.SendableAmount(40000, 36000, 90).ShouldBe(6000);
            OverflowDrainRules.SendableAmount(40000, 31000, 90).ShouldBe(1000);
        }

        [Fact]
        public void SendableAmount_IsZeroBelowTheTargetOrWithoutCapacity()
        {
            OverflowDrainRules.SendableAmount(40000, 30000, 90).ShouldBe(0);
            OverflowDrainRules.SendableAmount(40000, 10000, 90).ShouldBe(0);
            OverflowDrainRules.SendableAmount(0, 5000, 90).ShouldBe(0);
        }

        [Fact]
        public void SendableAmount_IsLargerThanTheOldAboveThresholdAmount()
        {
            // the old amount: 36,000 - 90 % of 40,000 = 0; right at the threshold the old code sent nothing worth a trip
            (OverflowDrainRules.SendableAmount(40000, 36000, 90) > 36000 - 40000 * 90 / 100).ShouldBeTrue();
        }
    }
}
