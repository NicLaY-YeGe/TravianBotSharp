using System.Collections.Concurrent;

namespace MainCore.Commands.Features.VillageOverview
{
    // 2026-11-11 (village overview tab, user request): what the bot last SAW of each village that is not in the database.
    // Resources, Smithy and Celebration come straight from the database / village settings; troops, merchants and the
    // training queue ends are only known from page reads, so they are remembered here:
    //   - troops: written by RefreshVillageOverviewTask (the Rally Point overview, "Read from game" button);
    //   - merchants: written by RefreshVillageOverviewTask and by every Marketplace visit that already happens
    //     (ToSendResourcePageCommand), no extra page load;
    //   - training queue end: written by RefreshVillageOverviewTask and by TrainTroopTask when it has read the queue.
    // Memory only on purpose (like ConstructionNeed / BuyCropMemory): a database table would need a hand-written schema
    // patch for every existing install (AppDbContext "schema patches"). After a restart the cells are "?" until the next
    // read; every snapshot carries its own read time so the tab can show how old it is.
    public sealed record TroopSnapshot(
        DateTime ReadAt,
        string[] Names,
        int[] Home,
        int[] Reinforcement,
        int[] Moving,
        int[] Returning,
        int[] Stationed,
        bool Complete);

    public sealed record MerchantSnapshot(DateTime ReadAt, int Free, int Total);

    public sealed record TrainQueueSnapshot(DateTime ReadAt, DateTime EndsAt);

    public static class VillageOverviewStore
    {
        private static readonly ConcurrentDictionary<int, TroopSnapshot> Troops = new();
        private static readonly ConcurrentDictionary<int, MerchantSnapshot> Merchants = new();
        private static readonly ConcurrentDictionary<(int VillageId, int Building), TrainQueueSnapshot> Queues = new();

        public static void SetTroops(int villageId, TroopSnapshot snapshot) => Troops[villageId] = snapshot;

        public static TroopSnapshot? GetTroops(int villageId) => Troops.TryGetValue(villageId, out var s) ? s : null;

        public static void SetMerchants(int villageId, int free, int total, DateTime? now = null)
        {
            if (total <= 0 || free < 0 || free > total) return;
            Merchants[villageId] = new MerchantSnapshot(now ?? DateTime.Now, free, total);
        }

        public static MerchantSnapshot? GetMerchants(int villageId) => Merchants.TryGetValue(villageId, out var s) ? s : null;

        // `building` is the BuildingEnums value as an int (keeps this class free of the enum's extension methods, so the
        // pure-test harness can compile it).
        public static void SetTrainQueue(int villageId, int building, int secondsRemaining, DateTime? now = null)
        {
            var time = now ?? DateTime.Now;
            Queues[(villageId, building)] = new TrainQueueSnapshot(time, time.AddSeconds(Math.Max(0, secondsRemaining)));
        }

        public static TrainQueueSnapshot? GetTrainQueue(int villageId, int building)
            => Queues.TryGetValue((villageId, building), out var s) ? s : null;

        public static void Clear(int villageId)
        {
            Troops.TryRemove(villageId, out _);
            Merchants.TryRemove(villageId, out _);
            foreach (var key in Queues.Keys.Where(x => x.VillageId == villageId).ToList())
            {
                Queues.TryRemove(key, out _);
            }
        }
    }
}
