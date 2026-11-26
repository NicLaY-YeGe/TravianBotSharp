using MainCore.Commands.Features.OasisScout;

namespace MainCore.Test.Commands.Features.OasisScout
{
    public class OasisScoutRulesTest
    {
        private static readonly DateTime Now = new(2026, 11, 25, 20, 0, 0);

        [Fact]
        public void ParkEmptyRun_ShortConfiguredInterval_IsLifted()
        {
            OasisScoutRules.ParkEmptyRun(Now, Now.AddMinutes(12), 0).ShouldBe(Now.AddMinutes(30));
        }

        [Fact]
        public void ParkEmptyRun_AddsTheJitter_ButCapsIt()
        {
            OasisScoutRules.ParkEmptyRun(Now, Now.AddMinutes(12), 7).ShouldBe(Now.AddMinutes(37));
            OasisScoutRules.ParkEmptyRun(Now, Now.AddMinutes(12), 99).ShouldBe(Now.AddMinutes(40));
        }

        [Fact]
        public void ParkEmptyRun_LongerConfiguredInterval_Wins()
        {
            OasisScoutRules.ParkEmptyRun(Now, Now.AddMinutes(90), 5).ShouldBe(Now.AddMinutes(90));
        }
    }
}
