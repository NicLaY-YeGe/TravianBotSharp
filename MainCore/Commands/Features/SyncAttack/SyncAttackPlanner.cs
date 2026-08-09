namespace MainCore.Commands.Features.SyncAttack
{
    // Pure calculation, no browser/DB access - deliberately kept separate from
    // SyncAttackPlanTask so it can be unit tested directly.
    public static class SyncAttackPlanner
    {
        // Extra headroom on top of the slowest village's travel time. Covers: TimerManager's
        // polling tick, and the fact that only one village's send can physically be submitted
        // at a time (single browser per account) - villages queued right after the slowest one
        // need a few seconds of margin so their own submission isn't still late.
        public static readonly TimeSpan SafetyBuffer = TimeSpan.FromSeconds(15);

        public sealed record PlanResult(DateTime ArrivalTime, Dictionary<VillageId, DateTime> SendTimes);

        public static Result<PlanResult> ComputeSendTimes(
            IReadOnlyDictionary<VillageId, TimeSpan> travelTimes,
            SyncAttackArrivalModeEnums mode,
            DateTime? desiredArrival,
            DateTime now)
        {
            if (travelTimes.Count == 0)
            {
                return Retry.Error.WithError("No source village has a probed travel time.");
            }

            var maxTravel = travelTimes.Values.Max();
            var earliestPossibleArrival = now.Add(maxTravel).Add(SafetyBuffer);

            DateTime arrival;
            if (mode == SyncAttackArrivalModeEnums.Specific)
            {
                if (desiredArrival is null)
                {
                    return Retry.Error.WithError("Specific arrival time was selected but no time was provided.");
                }

                if (desiredArrival.Value < earliestPossibleArrival)
                {
                    return Retry.Error.WithError(
                        $"Desired arrival time {desiredArrival.Value} is too soon. The slowest selected village needs " +
                        $"{maxTravel} to arrive; earliest possible common arrival is {earliestPossibleArrival}.");
                }

                arrival = desiredArrival.Value;
            }
            else
            {
                arrival = earliestPossibleArrival;
            }

            var sendTimes = new Dictionary<VillageId, DateTime>();
            foreach (var (villageId, travelTime) in travelTimes)
            {
                sendTimes[villageId] = arrival - travelTime;
            }

            return new PlanResult(arrival, sendTimes);
        }
    }
}
