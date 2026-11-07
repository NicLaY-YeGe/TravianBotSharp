using MainCore.Commands.Features.Scheduling;

namespace MainCore.Test.Commands.Features.Scheduling
{
    public class QueueDeferRulesTest
    {
        [Fact]
        public void BufferedSeconds_AddsThirtyToSixtySeconds()
        {
            for (var i = 0; i < 200; i++)
            {
                var value = QueueDeferRules.BufferedSeconds(100, new Random(i));
                (value >= 130 && value <= 160).ShouldBeTrue();
            }
        }

        [Fact]
        public void BufferedSecondsCapped_NeverExceedsCap()
        {
            QueueDeferRules.BufferedSecondsCapped(10 * 3600, new Random(1)).ShouldBe(QueueDeferRules.UncheckedQueueCapSeconds);
            (QueueDeferRules.BufferedSecondsCapped(60, new Random(1)) < 200).ShouldBeTrue();
        }

        [Fact]
        public void BusySeconds_RunningUsesExactEndPlusBuffer()
        {
            var value = QueueDeferRules.BusySeconds(1000, new Random(3));
            (value >= 1030 && value <= 1060).ShouldBeTrue();
        }

        [Fact]
        public void BusySeconds_NothingRunning_UsesLongUnknownRecheck()
        {
            for (var i = 0; i < 200; i++)
            {
                var value = QueueDeferRules.BusySeconds(null, new Random(i));
                (value >= 45 * 60 && value <= 60 * 60).ShouldBeTrue();
            }
            (QueueDeferRules.BusySeconds(0, new Random(1)) >= 45 * 60).ShouldBeTrue();
        }

        [Fact]
        public void IsTrapperAtCap_UnreadablePageIsNeverAtCap()
        {
            QueueDeferRules.IsTrapperAtCap(0, 0).ShouldBeFalse();
            QueueDeferRules.IsTrapperAtCap(0, 50).ShouldBeFalse();
            QueueDeferRules.IsTrapperAtCap(400, 399).ShouldBeFalse();
            QueueDeferRules.IsTrapperAtCap(400, 400).ShouldBeTrue();
            QueueDeferRules.IsTrapperAtCap(400, 450).ShouldBeTrue();
        }

        [Fact]
        public void NextTrapSeconds_AtCapIsAboutTwoHours()
        {
            var value = QueueDeferRules.NextTrapSeconds(null, true, 840, new Random(5));
            (value >= 120 * 60 && value <= 130 * 60).ShouldBeTrue();
            // the cap wins even if a countdown happens to be readable
            (QueueDeferRules.NextTrapSeconds(300, true, 840, new Random(5)) >= 120 * 60).ShouldBeTrue();
        }

        [Fact]
        public void NextTrapSeconds_RunningQueueWaitsForItsEnd()
        {
            var value = QueueDeferRules.NextTrapSeconds(300, false, 840, new Random(5));
            (value >= 330 && value <= 360).ShouldBeTrue();
        }

        [Fact]
        public void NextTrapSeconds_NothingKnownKeepsNormalRepeat()
        {
            QueueDeferRules.NextTrapSeconds(null, false, 840, new Random(5)).ShouldBe(840);
        }

        [Fact]
        public void NextTrainSeconds_NoBuildingKeepsNormalRepeat()
        {
            QueueDeferRules.NextTrainSeconds(new List<int?>(), 600, new Random(1)).ShouldBe(600);
        }

        [Fact]
        public void NextTrainSeconds_AllBusyWaitsForEarliestEnd()
        {
            var value = QueueDeferRules.NextTrainSeconds(new List<int?> { 5000, 1200 }, 600, new Random(1));
            (value >= 1230 && value <= 1260).ShouldBeTrue();
        }

        [Fact]
        public void NextTrainSeconds_StarvedBuildingKeepsNormalRepeat()
        {
            // Barracks busy for 2 h, Stable has nothing readable (no resources): the Stable's retry
            // must not be delayed by the Barracks queue.
            QueueDeferRules.NextTrainSeconds(new List<int?> { 7200, null }, 600, new Random(1)).ShouldBe(600);
        }

        [Fact]
        public void NextTrainSeconds_QueueEndsBeforeNormalRepeat_ComesEarlier()
        {
            var value = QueueDeferRules.NextTrainSeconds(new List<int?> { 120 }, 600, new Random(1));
            (value >= 150 && value <= 180).ShouldBeTrue();
        }

        [Fact]
        public void NextTrainSeconds_VeryLongQueueIsCapped()
        {
            QueueDeferRules.NextTrainSeconds(new List<int?> { 20 * 3600 }, 600, new Random(1))
                .ShouldBe(QueueDeferRules.UncheckedQueueCapSeconds);
        }

        [Fact]
        public void NextBuildingSeconds_Trained_ShortQueueDoesNotBeatTheInterval()
        {
            // queue ends in 90 s, the building's own interval is 30 min: no visit before the interval.
            QueueDeferRules.NextBuildingSeconds(true, 90, 1800, new Random(1)).ShouldBe(1800);
        }

        [Fact]
        public void NextBuildingSeconds_Trained_LongQueueWinsOverTheInterval()
        {
            var value = QueueDeferRules.NextBuildingSeconds(true, 7200, 1800, new Random(1));
            (value >= 7230 && value <= 7260).ShouldBeTrue();
        }

        [Fact]
        public void NextBuildingSeconds_Trained_UnreadableQueueUsesTheInterval()
        {
            QueueDeferRules.NextBuildingSeconds(true, null, 1800, new Random(1)).ShouldBe(1800);
        }

        [Fact]
        public void NextBuildingSeconds_Busy_UsesQueueEndPlusBuffer()
        {
            var value = QueueDeferRules.NextBuildingSeconds(false, 600, 1800, new Random(1));
            (value >= 630 && value <= 660).ShouldBeTrue();
        }

        [Fact]
        public void NextBuildingSeconds_Starved_UsesTheInterval()
        {
            QueueDeferRules.NextBuildingSeconds(false, null, 1800, new Random(1)).ShouldBe(1800);
        }
    }
}
