namespace MainCore.Commands.Features.ShareTrain
{
    // 2026-11-13, user decision ("pay ile egitim"): a SEPARATE training mode next to the old TrainTroopTask. No visit
    // intervals, no unit amounts. The only inputs are
    //   - the construction reserve floor (ConstructionReservePercent, must be > 0),
    //   - a WEIGHT per troop slot (0 = this troop is not trained in this mode),
    //   - a trigger (TrainShareTriggerPercent: train when the stock ABOVE the floor is worth at least this share of the
    //     storage capacity) and a queue ceiling (TrainShareQueueMaxMinutes per building, 0 = no ceiling).
    // Whenever the trigger is reached the whole stock above the floor is split between the weighted troops in
    // proportion to their weights, per resource. What a troop cannot use (a unit does not fit its share, the queue
    // ceiling is reached, the troop is not trainable) flows on to the others by the same weights ("waterfall"), round
    // after round, until nothing more fits. Arrays are always Wood / Clay / Iron / Crop. Pure, unit tested.
    public sealed record ShareCandidate(int Key, int Group, int Weight, long[] UnitCost, int UnitSeconds);

    public static class ShareTrainRules
    {
        public const int DefaultTriggerPercent = 20;
        public const int DefaultQueueMaxMinutes = 360;
        public const int MaxWeight = 1000;
        public const long NoBudget = long.MaxValue;

        // "Not before" gaps (minutes) of the task's memory: a visit that trained something, and one that trained nothing.
        public const int TrainedMinMinutes = 3;
        public const int TrainedMaxMinutes = 5;
        public const int NothingMinMinutes = 10;
        public const int NothingMaxMinutes = 20;

        private const int MaxRounds = 500;

        // The stock above the floor (per resource).
        public static long[] Surplus(long[] stock, long[] floor)
        {
            var surplus = new long[4];
            for (var i = 0; i < 4; i++) surplus[i] = Math.Max(0, stock[i] - Math.Max(0, floor[i]));
            return surplus;
        }

        // Trigger: the surplus (all four resources added up) is at least `percent` of the capacity that holds it
        // (3 x Warehouse + Granary). Percent <= 0 = always.
        public static bool ShouldRun(long[] surplus, long warehouse, long granary, int percent)
        {
            if (warehouse <= 0 || granary <= 0) return false;
            var total = surplus[0] + surplus[1] + surplus[2] + surplus[3];
            if (total <= 0) return false;
            if (percent <= 0) return true;

            var capacity = 3 * warehouse + granary;
            return total * 100 >= capacity * Math.Min(percent, 100);
        }

        // Queue seconds a building may still take before it reaches the ceiling (NoBudget = no ceiling).
        public static long GroupBudgetSeconds(int queueMaxMinutes, int queueSecondsNow)
        {
            if (queueMaxMinutes <= 0) return NoBudget;
            return Math.Max(0, queueMaxMinutes * 60L - Math.Max(0, queueSecondsNow));
        }

        // Units per candidate key. `budgets` = remaining queue seconds per group (a missing group = no ceiling).
        public static Dictionary<int, int> Allocate(IReadOnlyList<ShareCandidate> candidates, IReadOnlyDictionary<int, long> budgets, long[] surplus)
        {
            var units = new Dictionary<int, int>();
            foreach (var c in candidates) units[c.Key] = 0;

            var pool = (long[])surplus.Clone();
            var left = new Dictionary<int, long>(budgets);
            var order = candidates
                .Where(x => x.Weight > 0 && HasCost(x.UnitCost))
                .OrderByDescending(x => x.Weight)
                .ThenBy(x => x.Key)
                .ToList();

            for (var round = 0; round < MaxRounds; round++)
            {
                var active = order.Where(x => CanAddOne(x, pool, left)).ToList();
                if (active.Count == 0) break;

                var sumWeight = active.Sum(x => (long)Math.Min(x.Weight, MaxWeight));
                var start = (long[])pool.Clone();
                var progress = false;

                foreach (var c in active)
                {
                    var weight = Math.Min(c.Weight, MaxWeight);
                    var share = new long[4];
                    for (var i = 0; i < 4; i++) share[i] = start[i] * weight / sumWeight;

                    var n = Math.Min(Fit(share, c.UnitCost), Fit(pool, c.UnitCost));
                    n = Math.Min(n, BudgetUnits(c, left));
                    if (n <= 0) continue;

                    Take(c, n, pool, left);
                    units[c.Key] += n;
                    progress = true;
                }

                // No share was big enough for a whole unit but one still fits the pool: the heaviest troop takes it.
                if (!progress)
                {
                    var c = active[0];
                    Take(c, 1, pool, left);
                    units[c.Key] += 1;
                }
            }

            return units;
        }

        public static bool HasCost(long[] unitCost)
        {
            if (unitCost is null || unitCost.Length != 4) return false;
            return unitCost.Any(x => x > 0);
        }

        // "Not before" time after a visit.
        public static DateTime NextCheck(DateTime now, bool trainedAny, Random random)
        {
            var min = trainedAny ? TrainedMinMinutes : NothingMinMinutes;
            var max = trainedAny ? TrainedMaxMinutes : NothingMaxMinutes;
            return now.AddSeconds(random.Next(min * 60, max * 60 + 1));
        }

        private static bool CanAddOne(ShareCandidate c, long[] pool, Dictionary<int, long> left)
        {
            return Fit(pool, c.UnitCost) >= 1 && BudgetUnits(c, left) >= 1;
        }

        // How many whole units `stock` pays for (int.MaxValue-bounded).
        private static int Fit(long[] stock, long[] unitCost)
        {
            long fit = int.MaxValue;
            for (var i = 0; i < 4; i++)
            {
                if (unitCost[i] <= 0) continue;
                fit = Math.Min(fit, stock[i] / unitCost[i]);
            }
            return (int)Math.Max(0, fit);
        }

        private static int BudgetUnits(ShareCandidate c, Dictionary<int, long> left)
        {
            if (c.UnitSeconds <= 0) return int.MaxValue;
            if (!left.TryGetValue(c.Group, out var seconds) || seconds == NoBudget) return int.MaxValue;
            return (int)Math.Min(int.MaxValue, Math.Max(0, seconds / c.UnitSeconds));
        }

        private static void Take(ShareCandidate c, int n, long[] pool, Dictionary<int, long> left)
        {
            for (var i = 0; i < 4; i++) pool[i] -= c.UnitCost[i] * n;
            if (c.UnitSeconds > 0 && left.TryGetValue(c.Group, out var seconds) && seconds != NoBudget)
            {
                left[c.Group] = Math.Max(0, seconds - (long)c.UnitSeconds * n);
            }
        }
    }
}
