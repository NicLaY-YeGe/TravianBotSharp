namespace MainCore.Commands.Features.AttackCheck
{
    // 2026-10-24, user decision: the "attack check" looks at the village list every 4-6 minutes
    // (random, so the rhythm is not a fixed clock) and does nothing else when no village is under
    // attack. Pure so the range is unit tested; AttackCheckTask / NextExecuteAttackCheckTaskCommand use it.
    public static class AttackCheckRules
    {
        public const int MinSeconds = 4 * 60;
        public const int MaxSeconds = 6 * 60;

        public static TimeSpan NextDelay(Random random)
        {
            return TimeSpan.FromSeconds(random.Next(MinSeconds, MaxSeconds + 1));
        }
    }
}
