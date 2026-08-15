namespace MainCore.Commands.Features.WaveAttack
{
    // Pure calculation, no browser/DB access - deliberately kept separate from
    // WaveAttackPlanTask so it can be unit tested directly (same rationale as
    // SyncAttackPlanner).
    //
    // Mirrors SyncAttackPlanner's "probe real travel time, then back-calculate a send time"
    // approach, but for a single source village sending a SEQUENCE of waves (1 main + N
    // repeats) instead of multiple source villages sending once each. The two wave
    // compositions almost never travel at the same speed (a catapult-heavy main wave is much
    // slower than a small repeat wave), so each wave's send time has to be computed from its
    // OWN probed travel time to land at its target arrival slot - arrivals are spaced
    // GapSeconds apart, sends are not.
    public static class WaveAttackPlanner
    {
        // Same rationale as SyncAttackPlanner.SafetyBuffer: covers TimerManager's polling tick
        // and gives the bot time to actually load the page/type amounts/submit before the
        // computed send time for the main wave.
        public static readonly TimeSpan SafetyBuffer = TimeSpan.FromSeconds(15);

        // Below this, two consecutive waves' send times are close enough that the single
        // browser session may not physically have finished submitting the first before the
        // second is due. Not a hard failure - surfaced as a warning so the user can pick a
        // larger gap if they see it.
        public static readonly TimeSpan MinimumRecommendedSendGap = TimeSpan.FromSeconds(5);

        public sealed record WaveSchedule(int WaveIndex, bool IsMainWave, DateTime SendAt, DateTime ArrivalTime);

        public sealed record PlanResult(IReadOnlyList<WaveSchedule> Waves, IReadOnlyList<string> Warnings);

        public static Result<PlanResult> ComputeSchedule(
            TimeSpan mainWaveTravelTime,
            TimeSpan? repeatWaveTravelTime,
            int waveCount,
            int gapSeconds,
            DateTime now)
        {
            if (waveCount < 0)
            {
                return Retry.Error.WithError("Wave count cannot be negative.");
            }

            if (waveCount > 0 && repeatWaveTravelTime is null)
            {
                return Retry.Error.WithError("Wave count is greater than 0 but the repeat wave's travel time could not be probed.");
            }

            if (waveCount > 0 && gapSeconds <= 0)
            {
                return Retry.Error.WithError("Gap between waves must be greater than 0 seconds when wave count is greater than 0.");
            }

            var waves = new List<WaveSchedule>();
            var warnings = new List<string>();

            var mainArrival = now.Add(mainWaveTravelTime).Add(SafetyBuffer);
            var mainSendAt = mainArrival - mainWaveTravelTime;
            waves.Add(new WaveSchedule(0, IsMainWave: true, mainSendAt, mainArrival));

            var previousArrival = mainArrival;
            var previousSendAt = mainSendAt;

            for (var i = 1; i <= waveCount; i++)
            {
                var arrival = previousArrival.AddSeconds(gapSeconds);
                var sendAt = arrival - repeatWaveTravelTime!.Value;

                if (sendAt - previousSendAt < MinimumRecommendedSendGap)
                {
                    warnings.Add(
                        $"Wave {i}'s send time is only {(sendAt - previousSendAt).TotalSeconds:F1}s after the previous wave's - " +
                        "the bot may not physically have time to submit both in sequence. Consider a larger gap.");
                }

                waves.Add(new WaveSchedule(i, IsMainWave: false, sendAt, arrival));
                previousArrival = arrival;
                previousSendAt = sendAt;
            }

            return new PlanResult(waves, warnings);
        }
    }
}
