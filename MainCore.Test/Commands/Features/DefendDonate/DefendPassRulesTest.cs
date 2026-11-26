using MainCore.Commands.Features.DefendDonate;

namespace MainCore.Test.Commands.Features.DefendDonate
{
    public class DefendPassRulesTest
    {
        private static readonly DateTime Now = new(2026, 11, 26, 4, 26, 30);

        [Theory]
        [InlineData(true, 30, 90)]   // dodge send lead 30 s + 60
        [InlineData(true, 45, 105)]
        [InlineData(true, 0, 90)]    // unset lead = default 30 s
        [InlineData(false, 30, 75)]  // no dodge: 15 s reserve + 60
        public void FinalPassLead(bool dodge, int dodgeLead, int expected)
            => DefendPassRules.FinalPassLeadSeconds(dodge, dodgeLead).ShouldBe(expected);

        [Fact]
        public void Pass1_ImpactFarAway_Pass2IsNext()
        {
            var impact = Now.AddMinutes(60);
            var next = DefendPassRules.Next(1, Now, impact, 90);
            next.ShouldNotBeNull();
            next.Value.Pass.ShouldBe(2);
            next.Value.At.ShouldBe(impact.AddMinutes(-15));
        }

        [Fact]
        public void Pass1_ImpactIn12Min_GoesStraightToFinalPass() // the 2026-11-26 case: 729 s
        {
            var impact = Now.AddSeconds(729);
            var next = DefendPassRules.Next(1, Now, impact, 90);
            next.ShouldNotBeNull();
            next.Value.Pass.ShouldBe(3);
            next.Value.At.ShouldBe(impact.AddSeconds(-90));
        }

        [Fact]
        public void Pass2_IsFollowedByFinalPass()
        {
            var impact = Now.AddMinutes(15);
            var next = DefendPassRules.Next(2, Now, impact, 90);
            next.ShouldNotBeNull();
            next.Value.Pass.ShouldBe(3);
        }

        [Fact]
        public void FinalPass_IsNeverFollowedByAnother()
            => DefendPassRules.Next(3, Now, Now.AddMinutes(30), 90).ShouldBeNull();

        [Fact]
        public void Pass1_ImpactTooClose_NothingFits()
        {
            // final pass would start at impact - 90 s = 20 s from now: less than the minimum gap
            DefendPassRules.Next(1, Now, Now.AddSeconds(110), 90).ShouldBeNull();
        }

        [Fact]
        public void Pass2_Moment_NeverRepeatedAsPass2()
        {
            // pass 1 ran 14 min before impact: 15-min mark is in the past
            var impact = Now.AddMinutes(14);
            DefendPassRules.Next(1, Now, impact, 90)!.Value.Pass.ShouldBe(3);
        }
    }
}
