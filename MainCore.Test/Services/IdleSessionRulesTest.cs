using MainCore.Services;

namespace MainCore.Test.Services
{
    public class IdleSessionRulesTest
    {
        private static readonly DateTime Now = new(2026, 11, 15, 10, 0, 0);

        [Fact]
        public void NoPreviousActivity_NoRefresh()
        {
            IdleSessionRules.ShouldRefresh(null, Now).ShouldBeFalse();
        }

        [Fact]
        public void ShortGap_NoRefresh()
        {
            IdleSessionRules.ShouldRefresh(Now.AddMinutes(-18), Now).ShouldBeFalse();
            IdleSessionRules.ShouldRefresh(Now.AddMinutes(-29), Now).ShouldBeFalse();
        }

        [Fact]
        public void LongGap_Refreshes()
        {
            // 2026-11-15 log: last task 05:59:52, next 10:00:00.
            IdleSessionRules.ShouldRefresh(new DateTime(2026, 11, 15, 5, 59, 52), Now).ShouldBeTrue();
            IdleSessionRules.ShouldRefresh(Now.AddMinutes(-30), Now).ShouldBeTrue();
        }

        [Fact]
        public void AccountActivity_TouchGetClear()
        {
            AccountActivity.Get(987001).ShouldBeNull();
            AccountActivity.Touch(987001);
            AccountActivity.Get(987001).ShouldNotBeNull();
            AccountActivity.Clear(987001);
            AccountActivity.Get(987001).ShouldBeNull();
        }
    }
}
