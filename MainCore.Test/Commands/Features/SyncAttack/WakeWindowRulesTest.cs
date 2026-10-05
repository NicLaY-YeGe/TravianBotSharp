using MainCore.Commands.Features.SyncAttack;

namespace MainCore.Test.Commands.Features.SyncAttack
{
    public class WakeWindowRulesTest
    {
        private static readonly DateTime Now = new(2026, 10, 4, 3, 0, 0);

        private static WakeQueueEntry Normal(DateTime at) => new(at, false, false, null, null);
        private static WakeQueueEntry Login(DateTime at) => new(at, false, true, null, null);
        private static WakeQueueEntry WakeUp(DateTime at) => new(at, true, false, null, null);
        private static WakeQueueEntry Strike(DateTime sendAt, DateTime wakeStart, DateTime wakeEnd) => new(sendAt, true, false, wakeStart, wakeEnd);

        [Fact]
        public void RandomSpan_StaysInsideRange()
        {
            var random = new Random(1);
            for (var i = 0; i < 500; i++)
            {
                var span = WakeWindowRules.RandomSpan(5, 10, random);
                (span >= TimeSpan.FromMinutes(5)).ShouldBeTrue();
                (span <= TimeSpan.FromMinutes(10)).ShouldBeTrue();
            }
        }

        [Fact]
        public void RandomSpan_SwappedMinMax_IsFixed()
        {
            var span = WakeWindowRules.RandomSpan(10, 5, new Random(2));
            (span >= TimeSpan.FromMinutes(5)).ShouldBeTrue();
            (span <= TimeSpan.FromMinutes(10)).ShouldBeTrue();
        }

        [Fact]
        public void RandomSpan_NegativeAndHuge_AreClamped()
        {
            WakeWindowRules.RandomSpan(-5, -1, new Random(3)).ShouldBe(TimeSpan.Zero);
            (WakeWindowRules.RandomSpan(0, 99999, new Random(4)) <= TimeSpan.FromMinutes(600)).ShouldBeTrue();
        }

        [Fact]
        public void RandomSpan_EqualMinMax_IsExact()
        {
            WakeWindowRules.RandomSpan(7, 7, new Random(5)).ShouldBe(TimeSpan.FromMinutes(7));
        }

        [Fact]
        public void ComputeWindow_CoversEarliestAndLatestSend()
        {
            var sends = new[] { Now.AddMinutes(30), Now.AddMinutes(10), Now.AddMinutes(20) };
            var (start, end) = WakeWindowRules.ComputeWindow(sends, new WakeWindowOptions(5, 10, 5, 10), new Random(6));

            (start <= Now.AddMinutes(10).AddMinutes(-5)).ShouldBeTrue();
            (start >= Now.AddMinutes(10).AddMinutes(-10)).ShouldBeTrue();
            (end >= Now.AddMinutes(30).AddMinutes(5)).ShouldBeTrue();
            (end <= Now.AddMinutes(30).AddMinutes(10)).ShouldBeTrue();
        }

        [Fact]
        public void ComputeWindow_NoSends_Throws()
        {
            var threw = false;
            try
            {
                WakeWindowRules.ComputeWindow(Array.Empty<DateTime>(), WakeWindowOptions.Default, new Random(7));
            }
            catch (ArgumentException)
            {
                threw = true;
            }
            threw.ShouldBeTrue();
        }

        [Fact]
        public void HasOpenWindow_OnlyAfterStart()
        {
            WakeWindowRules.HasOpenWindow(new DateTime?[] { null, Now.AddMinutes(1) }, Now).ShouldBeFalse();
            WakeWindowRules.HasOpenWindow(new DateTime?[] { null, Now.AddMinutes(-1) }, Now).ShouldBeTrue();
            WakeWindowRules.HasOpenWindow(new DateTime?[] { Now }, Now).ShouldBeTrue();
        }

        [Fact]
        public void SelectIndex_OnlineHour_OnlyHeadWhenDue()
        {
            var queue = new[] { Normal(Now.AddMinutes(1)), WakeUp(Now.AddMinutes(-1)) };
            WakeWindowRules.SelectIndex(queue, Now, isOnlineHour: true).ShouldBe(-1);

            var due = new[] { Normal(Now.AddMinutes(-1)), Normal(Now.AddMinutes(1)) };
            WakeWindowRules.SelectIndex(due, Now, isOnlineHour: true).ShouldBe(0);
        }

        [Fact]
        public void SelectIndex_Empty_IsNone()
        {
            WakeWindowRules.SelectIndex(Array.Empty<WakeQueueEntry>(), Now, true).ShouldBe(-1);
            WakeWindowRules.SelectIndex(Array.Empty<WakeQueueEntry>(), Now, false).ShouldBe(-1);
        }

        [Fact]
        public void SelectIndex_OfflineHour_NormalTasksStayBlocked()
        {
            var queue = new[] { Normal(Now.AddMinutes(-5)), Login(Now.AddMinutes(-4)) };
            WakeWindowRules.SelectIndex(queue, Now, isOnlineHour: false).ShouldBe(-1);
        }

        [Fact]
        public void SelectIndex_OfflineHour_WakeUpTaskJumpsTheBlockedHead()
        {
            // The old head-only rule would have returned nothing here: a normal task that has
            // been waiting since the offline hours began sits in front of the wake-up task.
            var queue = new[] { Normal(Now.AddMinutes(-60)), WakeUp(Now.AddMinutes(-1)) };
            WakeWindowRules.SelectIndex(queue, Now, isOnlineHour: false).ShouldBe(1);
        }

        [Fact]
        public void SelectIndex_OfflineHour_LoginAllowedOnlyWhileWindowOpen()
        {
            var strike = Strike(Now.AddMinutes(6), Now.AddMinutes(-1), Now.AddMinutes(16));
            var login = Login(Now.AddMinutes(-2));

            WakeWindowRules.SelectIndex(new[] { login, strike }, Now, isOnlineHour: false).ShouldBe(0);

            var futureStrike = Strike(Now.AddMinutes(60), Now.AddMinutes(50), Now.AddMinutes(70));
            WakeWindowRules.SelectIndex(new[] { login, futureStrike }, Now, isOnlineHour: false).ShouldBe(-1);
        }

        [Fact]
        public void SelectIndex_OfflineHour_StrikeWaitsUntilItsSendTime()
        {
            var strike = Strike(Now.AddMinutes(6), Now.AddMinutes(-1), Now.AddMinutes(16));
            WakeWindowRules.SelectIndex(new[] { strike }, Now, isOnlineHour: false).ShouldBe(-1);
            WakeWindowRules.SelectIndex(new[] { strike }, Now.AddMinutes(6), isOnlineHour: false).ShouldBe(0);
        }

        [Fact]
        public void SelectIndex_OfflineHour_LateStrikeStillRuns()
        {
            // 10 minutes past its send time: still sent (user decision, 2026-10-03).
            var strike = Strike(Now.AddMinutes(-10), Now.AddMinutes(-16), Now.AddMinutes(0));
            WakeWindowRules.SelectIndex(new[] { strike }, Now, isOnlineHour: false).ShouldBe(0);
        }

        [Fact]
        public void ComputeAttackWindow_OpensBeforeFirstAction_ClosesAfterImpact()
        {
            var firstAction = Now.AddMinutes(60);
            var impact = Now.AddMinutes(61);
            var (start, end) = WakeWindowRules.ComputeAttackWindow(firstAction, impact, new WakeWindowOptions(5, 10, 5, 10), new Random(8));

            (start >= firstAction.AddMinutes(-10)).ShouldBeTrue();
            (start <= firstAction.AddMinutes(-5)).ShouldBeTrue();
            (end >= impact.AddMinutes(5)).ShouldBeTrue();
            (end <= impact.AddMinutes(10)).ShouldBeTrue();
        }

        [Fact]
        public void ComputeAttackWindow_ZeroAfter_NeverEndsBeforeFirstAction()
        {
            // Impact earlier than the first action (should not happen, but a stale reading
            // must not produce a window that closes before the action it exists for).
            var firstAction = Now.AddMinutes(10);
            var impact = Now.AddMinutes(5);
            var (_, end) = WakeWindowRules.ComputeAttackWindow(firstAction, impact, new WakeWindowOptions(5, 5, 0, 0), new Random(9));
            (end >= firstAction).ShouldBeTrue();
        }

        [Fact]
        public void NextSleepAfterWake_NoOpenWindow_IsNull()
        {
            var entries = new (DateTime?, DateTime?)[] { (null, null), (Now.AddMinutes(5), Now.AddMinutes(20)) };
            WakeWindowRules.NextSleepAfterWake(entries, Now).ShouldBeNull();
        }

        [Fact]
        public void NextSleepAfterWake_UsesLatestWindowEnd()
        {
            var entries = new (DateTime?, DateTime?)[]
            {
                (Now.AddMinutes(-5), Now.AddMinutes(10)),
                (Now.AddMinutes(-5), Now.AddMinutes(14)),
            };
            WakeWindowRules.NextSleepAfterWake(entries, Now).ShouldBe(Now.AddMinutes(14));
        }

        [Fact]
        public void NextSleepAfterWake_AlreadyOverdue_StillWaitsTwoMinutes()
        {
            var entries = new (DateTime?, DateTime?)[] { (Now.AddMinutes(-30), Now.AddMinutes(-10)) };
            WakeWindowRules.NextSleepAfterWake(entries, Now).ShouldBe(Now.AddMinutes(2));
        }
    }
}
