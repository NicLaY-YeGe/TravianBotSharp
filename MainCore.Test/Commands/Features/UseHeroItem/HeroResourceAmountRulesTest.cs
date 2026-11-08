using MainCore.Commands.Features.UseHeroItem;

namespace MainCore.Test.Commands.Features.UseHeroItem
{
    public class HeroResourceAmountRulesTest
    {
        [Theory]
        [InlineData(0, 0)]
        [InlineData(-5, 0)]
        [InlineData(1, 100)]
        [InlineData(99, 100)]
        [InlineData(100, 100)]   // was 200 in the old helper
        [InlineData(400, 400)]   // was 500
        [InlineData(401, 500)]
        [InlineData(1281, 1300)]
        public void RoundUpTo100_KeepsMultiplesOf100(long amount, long expected)
        {
            HeroResourceAmountRules.RoundUpTo100(amount).ShouldBe(expected);
        }

        [Fact]
        public void AmountToUse_RoundsUpWhenStockAllowsIt()
        {
            HeroResourceAmountRules.AmountToUse(321, 478).ShouldBe(400);
            HeroResourceAmountRules.AmountToUse(400, 400).ShouldBe(400);
        }

        [Fact]
        public void AmountToUse_NeverAboveStockWhileTheNeedIsCovered()
        {
            // 350 in stock, 321 needed: the old code asked for 400 and failed
            HeroResourceAmountRules.AmountToUse(321, 350).ShouldBe(350);
            HeroResourceAmountRules.AmountToUse(321, 321).ShouldBe(321);
        }

        [Fact]
        public void AmountToUse_StockBelowNeedStaysARealShortage()
        {
            // 478 clay, 745 needed (log 23:54): rounded amount is returned so validation reports the shortage
            HeroResourceAmountRules.AmountToUse(745, 478).ShouldBe(800);
            HeroResourceAmountRules.AmountToUse(100, 0).ShouldBe(100);
        }

        [Fact]
        public void AmountToUse_NoNeedMeansZero()
        {
            HeroResourceAmountRules.AmountToUse(0, 1000).ShouldBe(0);
        }

        [Fact]
        public void AmountsToUse_WorksPerResourceAndToleratesAShortStockArray()
        {
            var result = HeroResourceAmountRules.AmountsToUse(new long[] { 388, 310, 201, 0 }, new long[] { 1000, 350, 250, 0 });
            result.Length.ShouldBe(4);
            result[0].ShouldBe(400);
            result[1].ShouldBe(350);
            result[2].ShouldBe(250);
            result[3].ShouldBe(0);

            var short2 = HeroResourceAmountRules.AmountsToUse(new long[] { 100, 100 }, new long[] { 500 });
            short2[0].ShouldBe(100);
            short2[1].ShouldBe(100);
        }
    }
}
