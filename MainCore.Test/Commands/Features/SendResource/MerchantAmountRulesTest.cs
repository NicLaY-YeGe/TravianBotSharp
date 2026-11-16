using MainCore.Commands.Features.SendResource;

namespace MainCore.Test.Commands.Features.SendResource
{
    public class MerchantAmountRulesTest
    {
        [Fact]
        public void RoundDown_GivesWholeLoadsAndNeverMoreThanAsked()
        {
            MerchantAmountRules.RoundDown(0).ShouldBe(0);
            MerchantAmountRules.RoundDown(-5).ShouldBe(0);
            MerchantAmountRules.RoundDown(749).ShouldBe(0);
            MerchantAmountRules.RoundDown(750).ShouldBe(750);
            MerchantAmountRules.RoundDown(1499.9).ShouldBe(750);
            MerchantAmountRules.RoundDown(1500).ShouldBe(1500);
            MerchantAmountRules.RoundDown(8200).ShouldBe(7500);
        }

        [Fact]
        public void RoundUpWithin_RoundsUpWhileTheSourceCanSpareIt()
        {
            MerchantAmountRules.RoundUpWithin(800, 5000).ShouldBe(1500);
            MerchantAmountRules.RoundUpWithin(750, 5000).ShouldBe(750);
            MerchantAmountRules.RoundUpWithin(1, 5000).ShouldBe(750);
        }

        [Fact]
        public void RoundUpWithin_NeverExceedsTheCeiling()
        {
            // wants 800 but the source can only spare 1000: one full load is all it can send
            MerchantAmountRules.RoundUpWithin(800, 1000).ShouldBe(750);
            MerchantAmountRules.RoundUpWithin(800, 700).ShouldBe(0);
            MerchantAmountRules.RoundUpWithin(0, 5000).ShouldBe(0);
            MerchantAmountRules.RoundUpWithin(800, 0).ShouldBe(0);
        }

        [Fact]
        public void EveryResult_IsAMultipleOfTheLoad()
        {
            for (var wanted = 1; wanted < 9000; wanted += 137)
            {
                (MerchantAmountRules.RoundDown(wanted) % MerchantAmountRules.LoadPerMerchant).ShouldBe(0);
                (MerchantAmountRules.RoundUpWithin(wanted, 6000) % MerchantAmountRules.LoadPerMerchant).ShouldBe(0);
            }
        }
    }
}
