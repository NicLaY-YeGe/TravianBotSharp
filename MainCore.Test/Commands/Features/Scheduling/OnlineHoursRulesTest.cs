using MainCore.Commands.Features.Scheduling;

namespace MainCore.Test.Commands.Features.Scheduling
{
    public class OnlineHoursRulesTest
    {
        // Hours 8..22 online (08:00-22:59), 23 and 0..7 offline.
        private static int DayMask()
        {
            var mask = 0;
            for (var hour = 8; hour <= 22; hour++) mask |= 1 << hour;
            return mask;
        }

        [Fact]
        public void IsOnline_ReadsTheBitOfTheHour()
        {
            var mask = DayMask();
            OnlineHoursRules.IsOnline(mask, 8).ShouldBeTrue();
            OnlineHoursRules.IsOnline(mask, 22).ShouldBeTrue();
            OnlineHoursRules.IsOnline(mask, 23).ShouldBeFalse();
            OnlineHoursRules.IsOnline(mask, 7).ShouldBeFalse();
            OnlineHoursRules.IsOnline(mask, 24).ShouldBeFalse();
            OnlineHoursRules.IsOnline(mask, -1).ShouldBeFalse();
        }

        [Fact]
        public void NextOfflineStart_AllHoursOnline_IsNull()
        {
            OnlineHoursRules.NextOfflineStart(OnlineHoursRules.AllHours, new DateTime(2026, 10, 30, 12, 30, 0)).ShouldBeNull();
        }

        [Fact]
        public void NextOfflineStart_OnlineNow_IsTheNextOfflineHourBoundary()
        {
            var result = OnlineHoursRules.NextOfflineStart(DayMask(), new DateTime(2026, 10, 30, 12, 30, 0));
            result.ShouldBe(new DateTime(2026, 10, 30, 23, 0, 0));
        }

        [Fact]
        public void NextOfflineStart_OfflineNow_IsNow()
        {
            var now = new DateTime(2026, 10, 30, 3, 15, 0);
            OnlineHoursRules.NextOfflineStart(DayMask(), now).ShouldBe(now);
        }

        [Fact]
        public void NextOfflineStart_CrossesMidnight()
        {
            // Online 20..23 and 0..1, offline 2..19: at 23:30 the next offline hour starts at 02:00 the next day.
            var mask = 0;
            for (var hour = 20; hour <= 23; hour++) mask |= 1 << hour;
            mask |= 1 << 0;
            mask |= 1 << 1;
            OnlineHoursRules.NextOfflineStart(mask, new DateTime(2026, 10, 30, 23, 30, 0)).ShouldBe(new DateTime(2026, 10, 31, 2, 0, 0));
        }

        [Fact]
        public void NextOnlineStart_OnlineNow_IsNow()
        {
            var now = new DateTime(2026, 10, 30, 12, 30, 0);
            OnlineHoursRules.NextOnlineStart(DayMask(), now).ShouldBe(now);
        }

        [Fact]
        public void NextOnlineStart_FromAnOfflineHour_IsTheNextOnlineBoundary()
        {
            OnlineHoursRules.NextOnlineStart(DayMask(), new DateTime(2026, 10, 30, 23, 0, 0)).ShouldBe(new DateTime(2026, 10, 31, 8, 0, 0));
            OnlineHoursRules.NextOnlineStart(DayMask(), new DateTime(2026, 10, 30, 3, 20, 0)).ShouldBe(new DateTime(2026, 10, 30, 8, 0, 0));
        }

        [Fact]
        public void NextOnlineStart_NeverOnline_IsNull()
        {
            OnlineHoursRules.NextOnlineStart(0, new DateTime(2026, 10, 30, 3, 20, 0)).ShouldBeNull();
        }
    }
}
