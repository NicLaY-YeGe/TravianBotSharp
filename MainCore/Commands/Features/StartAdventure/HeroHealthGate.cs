using System.Collections.Concurrent;

namespace MainCore.Commands.Features.StartAdventure
{
    // 2026-10-24, user decision: when the hero's health is below the account's
    // MinHeroHealthPercent (the same setting the adventure uses) the hero must not go anywhere -
    // adventures AND Oasis scout are skipped. Reading the health costs a page load
    // (/hero/attributes), so the last reading is remembered for a few minutes and shared by both
    // tasks. In memory on purpose: after a restart the next task simply reads it again.
    public static class HeroHealthGate
    {
        // How long a reading is trusted without loading the hero page again.
        public const int FreshMinutes = 10;

        // A task skipped because of low health looks again after at least this long.
        public const int RecheckMinutes = 30;

        // The health page could not be read at all (timeout, odd page): the hero is left where it is
        // for this run (never guess "healthy"), and the task looks again after this shorter wait.
        public const int FailedReadRecheckMinutes = 10;

        private static readonly ConcurrentDictionary<int, (int Percent, DateTime At)> Readings = new();

        // 0 (or less) = the user set no restriction.
        public static bool IsBelowMinimum(int healthPercent, int minimumPercent)
        {
            return minimumPercent > 0 && healthPercent < minimumPercent;
        }

        public static void Record(int accountId, int healthPercent, DateTime now)
        {
            Readings[accountId] = (healthPercent, now);
        }

        public static bool TryGetFresh(int accountId, DateTime now, out int healthPercent)
        {
            if (Readings.TryGetValue(accountId, out var reading)
                && now >= reading.At
                && (now - reading.At).TotalMinutes < FreshMinutes)
            {
                healthPercent = reading.Percent;
                return true;
            }

            healthPercent = 0;
            return false;
        }

        public static void Forget(int accountId)
        {
            Readings.TryRemove(accountId, out _);
        }
    }
}
