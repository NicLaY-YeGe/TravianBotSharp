using System.Collections.Concurrent;

namespace MainCore.Services
{
    // 2026-10-29: AccountTaskBehavior answers a "page is still navigating" read error with Skip + 10 s and keeps the
    // browser open (2026-10-28 fix). Without a limit a page that is stuck for good would be retried every 10 s
    // forever, with Information lines only; the old behaviour (close and re-open the browser) at least recovered.
    // This counts the CONSECUTIVE transient failures per account; AccountTaskBehavior gives up after
    // BrowserErrorRules.MaxConsecutiveTransient and treats the browser as closed. Memory only, pure, unit tested.
    public static class TransientStreak
    {
        private static readonly ConcurrentDictionary<int, int> Counts = new();

        public static int Increment(int accountId) => Counts.AddOrUpdate(accountId, 1, (_, current) => current + 1);

        public static void Reset(int accountId) => Counts.TryRemove(accountId, out _);

        public static int Get(int accountId) => Counts.TryGetValue(accountId, out var value) ? value : 0;
    }
}
