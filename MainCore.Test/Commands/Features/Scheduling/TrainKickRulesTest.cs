using MainCore.Commands.Features.Scheduling;

namespace MainCore.Test.Commands.Features.Scheduling
{
    public class TrainKickRulesTest
    {
        private static readonly DateTime Now = new(2026, 11, 7, 12, 0, 0);

        [Fact]
        public void LaterTask_IsMovedToTenToTwentySecondsFromNow()
        {
            for (var i = 0; i < 50; i++)
            {
                var kick = TrainKickRules.KickTime(Now, Now.AddHours(2), new Random(i));
                kick.ShouldNotBeNull();
                var seconds = (kick!.Value - Now).TotalSeconds;
                (seconds >= TrainKickRules.MinDelaySeconds).ShouldBeTrue();
                (seconds <= TrainKickRules.MaxDelaySeconds).ShouldBeTrue();
            }
        }

        [Fact]
        public void TaskThatRunsSoonerOrAtTheSameTime_IsLeftAlone()
        {
            TrainKickRules.KickTime(Now, Now.AddSeconds(5), new Random(1)).ShouldBeNull();
            TrainKickRules.KickTime(Now, Now.AddSeconds(-30), new Random(1)).ShouldBeNull();
        }

        [Fact]
        public void MissingTask_GetsAKickTime()
        {
            TrainKickRules.KickTime(Now, null, new Random(1)).ShouldNotBeNull();
        }
    }
}
