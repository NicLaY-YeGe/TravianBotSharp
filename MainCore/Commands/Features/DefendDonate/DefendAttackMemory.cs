using System.Collections.Concurrent;

namespace MainCore.Commands.Features.DefendDonate
{
    // 2026-10-19 (user decision): DefendDonateTask runs ONCE per incoming attack - it must not
    // re-check/re-run before the attack lands, even when it failed or was skipped. The task is
    // re-added to the queue by UpdateStorageCommand / UpdateVillageListCommand whenever the
    // village shows as under attack and no such task is queued, so "finished" alone is not
    // enough: this remembers, per village, until when the current attack counts as handled.
    // In-memory on purpose: after an app restart a single extra run is harmless.
    public static class DefendAttackMemory
    {
        private static readonly ConcurrentDictionary<int, DateTime> HandledUntil = new();

        public static void MarkHandled(int villageId, DateTime until, DateTime now)
        {
            foreach (var (id, expiry) in HandledUntil)
            {
                if (expiry <= now) HandledUntil.TryRemove(id, out _);
            }

            HandledUntil[villageId] = until;
        }

        public static bool IsHandled(int villageId, DateTime now)
            => HandledUntil.TryGetValue(villageId, out var until) && until > now;

        public static void Clear() => HandledUntil.Clear();
    }
}
