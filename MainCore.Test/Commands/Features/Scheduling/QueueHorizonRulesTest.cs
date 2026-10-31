using MainCore.Commands.Features.Scheduling;

namespace MainCore.Test.Commands.Features.Scheduling
{
    public class QueueHorizonRulesTest
    {
        private static readonly DateTime Now = new(2026, 10, 30, 12, 0, 0);

        [Fact]
        public void FromMinutes_ConvertsAndKeepsMinBelowTarget()
        {
            var horizon = QueueHorizon.FromMinutes(10, 60);
            horizon.MinSeconds.ShouldBe(600);
            horizon.TargetSeconds.ShouldBe(3600);
            horizon.Enabled.ShouldBeTrue();

            QueueHorizon.FromMinutes(90, 60).MinSeconds.ShouldBe(3600);
            QueueHorizon.FromMinutes(-5, -5).Enabled.ShouldBeFalse();
            QueueHorizon.FromMinutes(10, 0).Enabled.ShouldBeFalse();
        }

        [Fact]
        public void FillTo_NoOfflineStretch_IsTheTarget()
        {
            var horizon = QueueHorizon.FromMinutes(10, 60);
            QueueHorizonRules.FillToSeconds(Now, horizon, null, null, 180 * 60).ShouldBe(3600);
        }

        [Fact]
        public void FillTo_OfflineStillFarAway_IsTheTarget()
        {
            var horizon = QueueHorizon.FromMinutes(10, 60);
            var offline = Now.AddHours(5);
            var online = Now.AddHours(13);
            QueueHorizonRules.FillToSeconds(Now, horizon, offline, online, 180 * 60).ShouldBe(3600);
        }

        [Fact]
        public void FillTo_CloseToOffline_CoversTheGapButNeverBeyondTheCap()
        {
            var horizon = QueueHorizon.FromMinutes(10, 60);
            var offline = Now.AddMinutes(5);
            var online = Now.AddHours(8);

            // 8 h of offline would need 480 min; the cap of 180 min limits it.
            QueueHorizonRules.FillToSeconds(Now, horizon, offline, online, 180 * 60).ShouldBe(180 * 60);

            // A short gap is covered exactly: online in 100 min -> 100 min.
            QueueHorizonRules.FillToSeconds(Now, horizon, Now.AddMinutes(5), Now.AddMinutes(100), 180 * 60).ShouldBe(100 * 60);
        }

        [Fact]
        public void FillTo_CapBelowTarget_NeverShortensTheTarget()
        {
            var horizon = QueueHorizon.FromMinutes(10, 60);
            QueueHorizonRules.FillToSeconds(Now, horizon, Now.AddMinutes(5), Now.AddHours(8), 30 * 60).ShouldBe(3600);
        }

        [Fact]
        public void FillTo_CapZero_DisablesTheExtension()
        {
            var horizon = QueueHorizon.FromMinutes(10, 60);
            QueueHorizonRules.FillToSeconds(Now, horizon, Now.AddMinutes(5), Now.AddHours(8), 0).ShouldBe(3600);
        }

        [Fact]
        public void FillTo_AlreadyOffline_CoversUntilTheBotIsBack()
        {
            var horizon = QueueHorizon.FromMinutes(10, 60);
            // Running in an offline hour (wake window): the offline start is now.
            QueueHorizonRules.FillToSeconds(Now, horizon, Now, Now.AddMinutes(120), 180 * 60).ShouldBe(120 * 60);
        }

        [Fact]
        public void NeedSeconds_IsTheMissingPartOfTheQueue()
        {
            QueueHorizonRules.NeedSeconds(3600, 900).ShouldBe(2700);
            QueueHorizonRules.NeedSeconds(3600, 3600).ShouldBe(0);
            QueueHorizonRules.NeedSeconds(3600, 7200).ShouldBe(0);
            QueueHorizonRules.NeedSeconds(3600, -5).ShouldBe(3600);
        }

        [Fact]
        public void UnitsFor_RoundsUpAndRespectsTheCap()
        {
            QueueHorizonRules.UnitsFor(2700, 347, 0).ShouldBe(8);   // 7.78 -> 8
            QueueHorizonRules.UnitsFor(2776, 347, 0).ShouldBe(8);   // exactly 8 x 347
            QueueHorizonRules.UnitsFor(2777, 347, 0).ShouldBe(9);
            QueueHorizonRules.UnitsFor(2700, 347, 5).ShouldBe(5);
            QueueHorizonRules.UnitsFor(0, 347, 0).ShouldBe(0);
            QueueHorizonRules.UnitsFor(2700, 0, 0).ShouldBe(0);
        }

        [Fact]
        public void NextVisit_IsWhenTheQueueHasTheMinimumLeft()
        {
            var next = QueueHorizonRules.NextVisit(Now, 3600, 600, null);
            next.ShouldBe(Now.AddSeconds(3000));
        }

        [Fact]
        public void NextVisit_NeverCloserThanTheMinimumGap()
        {
            QueueHorizonRules.NextVisit(Now, 100, 600, null).ShouldBe(Now.AddSeconds(QueueHorizonRules.MinVisitGapSeconds));
            QueueHorizonRules.NextVisit(Now, 0, 0, null).ShouldBe(Now.AddSeconds(QueueHorizonRules.MinVisitGapSeconds));
        }

        [Fact]
        public void NextVisit_RegularVisitWouldFallIntoTheOfflineStretch_ComesBackAtTheLeadPoint()
        {
            // Offline starts in 60 min; the queue would be revisited in 50 min, which is later than the lead point (50 min = 60 - 10)
            // only when the queue lasts longer. Queue 70 min, min 10 -> planned 60 min > lead 50 min -> lead point.
            var offline = Now.AddMinutes(60);
            var next = QueueHorizonRules.NextVisit(Now, 70 * 60, 10 * 60, offline);
            next.ShouldBe(offline.AddSeconds(-QueueHorizonRules.PreOfflineLeadSeconds));
        }

        [Fact]
        public void NextVisit_RegularVisitIsBeforeTheLeadPoint_Stays()
        {
            var offline = Now.AddMinutes(180);
            QueueHorizonRules.NextVisit(Now, 60 * 60, 10 * 60, offline).ShouldBe(Now.AddSeconds(50 * 60));
        }

        [Fact]
        public void NextVisit_AlreadyInsideTheLeadWindow_KeepsTheRegularTime()
        {
            // The long fill was just done: the lead point is already behind us, so the planned time is used (the
            // scheduler simply holds it until the bot works again).
            var offline = Now.AddMinutes(5);
            var next = QueueHorizonRules.NextVisit(Now, 180 * 60, 10 * 60, offline);
            next.ShouldBe(Now.AddSeconds(170 * 60));
        }

        [Fact]
        public void StarvedRetry_IsFiveToTenMinutes()
        {
            for (var i = 0; i < 200; i++)
            {
                var value = QueueHorizonRules.StarvedRetrySeconds(new Random(i));
                (value >= 300 && value <= 600).ShouldBeTrue();
            }
        }
    }
}
