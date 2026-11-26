namespace MainCore.Commands.Features.Scheduling
{
    // 2026-11-07, user decision: right after a building has been started, what is left of the village's resources should
    // not wait for the troop training's own (possibly hours away) schedule. UpgradeBuildingTask asks for a visit of the
    // village's TrainTroopTask 10-20 s later. It only ever MOVES the task EARLIER; the per-troop / per-building
    // "not again before" intervals, busy queues, the horizon and the construction reserve all still apply at that visit
    // (the kick says "look now", it does not force a batch). Pure, unit tested.
    public static class TrainKickRules
    {
        public const int MinDelaySeconds = 10;
        public const int MaxDelaySeconds = 20;

        // The time to move the task to, or null when it already runs that soon (or sooner).
        public static DateTime? KickTime(DateTime now, DateTime? currentExecuteAt, Random random)
        {
            var kick = now.AddSeconds(random.Next(MinDelaySeconds, MaxDelaySeconds + 1));
            if (currentExecuteAt is { } current && current <= kick) return null;
            return kick;
        }
    }
}
