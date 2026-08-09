namespace MainCore.Enums
{
    public enum SyncAttackArrivalModeEnums
    {
        // Bot picks the earliest arrival time every selected village can make (bounded by the
        // slowest village's probed travel time + a small safety buffer).
        Earliest,

        // User provides an exact arrival date/time; bot validates it's reachable by the
        // slowest village before scheduling anything.
        Specific,
    }
}
