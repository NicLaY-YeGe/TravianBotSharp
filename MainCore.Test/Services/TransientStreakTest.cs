using MainCore.Services;

namespace MainCore.Test.Services
{
    public class TransientStreakTest
    {
        [Fact]
        public void Increment_CountsPerAccountAndResetClears()
        {
            const int a = 910001;
            const int b = 910002;
            TransientStreak.Reset(a);
            TransientStreak.Reset(b);

            TransientStreak.Increment(a).ShouldBe(1);
            TransientStreak.Increment(a).ShouldBe(2);
            TransientStreak.Increment(b).ShouldBe(1);
            TransientStreak.Get(a).ShouldBe(2);

            TransientStreak.Reset(a);
            TransientStreak.Get(a).ShouldBe(0);
            TransientStreak.Get(b).ShouldBe(1);
            TransientStreak.Reset(b);
        }

        [Fact]
        public void ShouldGiveUp_OnlyAtTheLimit()
        {
            BrowserErrorRules.ShouldGiveUp(0).ShouldBeFalse();
            BrowserErrorRules.ShouldGiveUp(BrowserErrorRules.MaxConsecutiveTransient - 1).ShouldBeFalse();
            BrowserErrorRules.ShouldGiveUp(BrowserErrorRules.MaxConsecutiveTransient).ShouldBeTrue();
            BrowserErrorRules.ShouldGiveUp(BrowserErrorRules.MaxConsecutiveTransient + 3).ShouldBeTrue();
        }
    }
}
