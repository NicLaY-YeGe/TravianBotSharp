using MainCore.Commands.Features.StartAdventure;

namespace MainCore.Test.Commands.Features.StartAdventure
{
    public class HeroHealthGateTest
    {
        private static readonly DateTime Now = new(2026, 10, 25, 12, 0, 0);

        [Fact]
        public void IsBelowMinimum_ZeroMinimumMeansNoRestriction()
        {
            HeroHealthGate.IsBelowMinimum(0, 0).ShouldBeFalse();
            HeroHealthGate.IsBelowMinimum(5, 0).ShouldBeFalse();
        }

        [Fact]
        public void IsBelowMinimum_DeadHeroIsBelowAnyMinimum()
        {
            HeroHealthGate.IsBelowMinimum(0, 40).ShouldBeTrue();
            HeroHealthGate.IsBelowMinimum(39, 40).ShouldBeTrue();
            HeroHealthGate.IsBelowMinimum(40, 40).ShouldBeFalse();
            HeroHealthGate.IsBelowMinimum(100, 40).ShouldBeFalse();
        }

        [Fact]
        public void Reading_IsTrustedForTenMinutesOnly()
        {
            HeroHealthGate.Forget(9001);
            HeroHealthGate.Record(9001, 55, Now);

            HeroHealthGate.TryGetFresh(9001, Now.AddMinutes(9), out var percent).ShouldBeTrue();
            percent.ShouldBe(55);
            HeroHealthGate.TryGetFresh(9001, Now.AddMinutes(10), out _).ShouldBeFalse();
        }

        [Fact]
        public void Reading_IsPerAccountAndForgettable()
        {
            HeroHealthGate.Forget(9002);
            HeroHealthGate.Forget(9003);
            HeroHealthGate.Record(9002, 0, Now);

            HeroHealthGate.TryGetFresh(9003, Now, out _).ShouldBeFalse();
            HeroHealthGate.TryGetFresh(9002, Now, out _).ShouldBeTrue();
            HeroHealthGate.Forget(9002);
            HeroHealthGate.TryGetFresh(9002, Now, out _).ShouldBeFalse();
        }

        [Fact]
        public void Reading_FromTheFutureIsNotTrusted()
        {
            HeroHealthGate.Forget(9004);
            HeroHealthGate.Record(9004, 80, Now);
            HeroHealthGate.TryGetFresh(9004, Now.AddMinutes(-1), out _).ShouldBeFalse();
        }
    }
}
