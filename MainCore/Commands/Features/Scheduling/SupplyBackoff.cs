using System.Collections.Concurrent;

namespace MainCore.Commands.Features.Scheduling
{
    // 2026-11-02 (live log 2026-11-02 00:00-10:46): a needy village asked for supply after every storage update, and
    // the source village answered "nothing spare to send (reserve protected)" 616 times out of 622 runs - each run a
    // Marketplace visit of 10-15 s in the single queue. When a source has just said it has nothing spare for a target,
    // it is not asked again for that target for a while. Memory only (a restart asks again, the old behaviour).
    // Pure (no browser, no database), unit tested.
    public static class SupplyBackoff
    {
        public const int MinSeconds = 8 * 60;
        public const int MaxSeconds = 12 * 60;

        private static readonly ConcurrentDictionary<(int Source, int Target), DateTime> Until = new();

        public static int Seconds(Random random)
        {
            return random.Next(MinSeconds, MaxSeconds + 1);
        }

        public static void Block(VillageId source, VillageId target, DateTime until)
        {
            Until[(source.Value, target.Value)] = until;
        }

        public static bool IsBlocked(VillageId source, VillageId target, DateTime now)
        {
            return Until.TryGetValue((source.Value, target.Value), out var until) && until > now;
        }

        public static void Clear(VillageId source, VillageId target)
        {
            Until.TryRemove((source.Value, target.Value), out _);
        }
    }
}
