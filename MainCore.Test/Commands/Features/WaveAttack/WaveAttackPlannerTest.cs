using MainCore.Commands.Features.WaveAttack;

namespace MainCore.Test.Commands.Features.WaveAttack
{
    public class WaveAttackPlannerTest
    {
        [Fact]
        public void MainWaveOnly_NoRepeats_SchedulesJustTheMainWave()
        {
            var now = new DateTime(2026, 1, 1, 12, 0, 0);
            var mainTravelTime = TimeSpan.FromMinutes(10);

            var result = WaveAttackPlanner.ComputeSchedule(mainTravelTime, null, waveCount: 0, gapSeconds: 1, now);

            Assert.True(result.IsSuccess);
            var waves = result.Value.Waves;
            Assert.Single(waves);
            Assert.True(waves[0].IsMainWave);
            Assert.Equal(now + mainTravelTime + WaveAttackPlanner.SafetyBuffer, waves[0].ArrivalTime);
            Assert.Equal(now + WaveAttackPlanner.SafetyBuffer, waves[0].SendAt);
        }

        [Fact]
        public void RepeatWaves_LandExactlyGapSecondsApart()
        {
            var now = new DateTime(2026, 1, 1, 12, 0, 0);
            var mainTravelTime = TimeSpan.FromMinutes(10);
            var repeatTravelTime = TimeSpan.FromMinutes(9); // a small wave travels faster than a catapult-heavy main wave

            var result = WaveAttackPlanner.ComputeSchedule(mainTravelTime, repeatTravelTime, waveCount: 3, gapSeconds: 2, now);

            Assert.True(result.IsSuccess);
            var waves = result.Value.Waves;
            Assert.Equal(4, waves.Count); // main + 3 repeats

            for (var i = 1; i < waves.Count; i++)
            {
                var gap = waves[i].ArrivalTime - waves[i - 1].ArrivalTime;
                Assert.Equal(TimeSpan.FromSeconds(2), gap);
            }

            // A faster repeat wave has to be SENT LATER than the main wave despite arriving
            // earlier in relative terms, since it needs less travel time to hit its slot.
            for (var i = 1; i < waves.Count; i++)
            {
                Assert.Equal(waves[i].ArrivalTime - repeatTravelTime, waves[i].SendAt);
            }
        }

        [Fact]
        public void WaveCountPositive_WithoutRepeatTravelTime_Fails()
        {
            var now = new DateTime(2026, 1, 1, 12, 0, 0);

            var result = WaveAttackPlanner.ComputeSchedule(TimeSpan.FromMinutes(10), null, waveCount: 2, gapSeconds: 1, now);

            Assert.True(result.IsFailed);
        }

        [Fact]
        public void WaveCountPositive_WithZeroGap_Fails()
        {
            var now = new DateTime(2026, 1, 1, 12, 0, 0);

            var result = WaveAttackPlanner.ComputeSchedule(TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(9), waveCount: 2, gapSeconds: 0, now);

            Assert.True(result.IsFailed);
        }

        [Fact]
        public void NegativeWaveCount_Fails()
        {
            var now = new DateTime(2026, 1, 1, 12, 0, 0);

            var result = WaveAttackPlanner.ComputeSchedule(TimeSpan.FromMinutes(10), null, waveCount: -1, gapSeconds: 1, now);

            Assert.True(result.IsFailed);
        }

        [Fact]
        public void TooSmallGap_ProducesWarningButStillSucceeds()
        {
            var now = new DateTime(2026, 1, 1, 12, 0, 0);
            // main and repeat travel times identical -> consecutive send times end up only
            // gapSeconds apart, which is below MinimumRecommendedSendGap for a 1s gap.
            var travelTime = TimeSpan.FromMinutes(5);

            var result = WaveAttackPlanner.ComputeSchedule(travelTime, travelTime, waveCount: 2, gapSeconds: 1, now);

            Assert.True(result.IsSuccess);
            Assert.NotEmpty(result.Value.Warnings);
        }
    }
}
