using MainCore.Commands.Features.RaidListScheduling;

namespace MainCore.Test.Commands.Features.RaidListScheduling
{
    public class RaidListShortageRulesTest
    {
        private static readonly DateTime Now = new(2026, 11, 11, 0, 27, 0);

        // the streak memory is static: every test uses its own row id
        [Fact]
        public void FirstShortagesOnlyDeferTheRow_TheSixthPausesTheList()
        {
            const int row = 920001;
            for (var i = 1; i < RaidListShortageRules.MaxConsecutiveBeforePause; i++)
            {
                var streak = RaidListShortageRules.RecordShortage(row);
                streak.ShouldBe(i);
                RaidListShortageRules.ShouldPauseList(streak).ShouldBeFalse();
            }

            var last = RaidListShortageRules.RecordShortage(row);
            RaidListShortageRules.ShouldPauseList(last).ShouldBeTrue();
        }

        [Fact]
        public void ASuccessfulSend_EndsTheStreak()
        {
            const int row = 920002;
            RaidListShortageRules.RecordShortage(row);
            RaidListShortageRules.RecordShortage(row);
            RaidListShortageRules.Streak(row).ShouldBe(2);

            RaidListShortageRules.Reset(row);

            RaidListShortageRules.Streak(row).ShouldBe(0);
            RaidListShortageRules.RecordShortage(row).ShouldBe(1);
        }

        [Fact]
        public void RowsCountSeparately()
        {
            const int a = 920003;
            const int b = 920004;
            RaidListShortageRules.RecordShortage(a);
            RaidListShortageRules.RecordShortage(a);
            RaidListShortageRules.RecordShortage(b).ShouldBe(1);
        }

        [Fact]
        public void NextTry_StaysInsideTheRetryWindow()
        {
            for (var i = 0; i < 50; i++)
            {
                var minutes = (RaidListShortageRules.NextTry(Now, new Random(i)) - Now).TotalMinutes;
                (minutes >= RaidListShortageRules.RetryMinMinutes).ShouldBeTrue();
                (minutes <= RaidListShortageRules.RetryMaxMinutes).ShouldBeTrue();
            }
        }
    }
}
