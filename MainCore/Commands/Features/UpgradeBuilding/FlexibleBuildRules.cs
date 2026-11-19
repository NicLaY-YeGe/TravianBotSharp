namespace MainCore.Commands.Features.UpgradeBuilding
{
    // 2026-11-19 (user decision): FLEXIBLE build order. The build list is worked strictly from the top; when the first job lacks
    // resources the whole village waits for it, even if a job further down could be paid for right now. With the village
    // setting FlexibleBuildOrderEnable the task looks behind the first job once that wait is longer than
    // FlexibleBuildOrderWaitMinutes (default 20) and builds the first job it can pay for RIGHT NOW - own stock plus the hero
    // inventory (when hero resources are allowed for building). The first job keeps its place and is tried again afterwards.
    // No starvation guard (user decision: these villages exist to feed the hammer village).
    //
    // Pure and array-based (resource arrays are wood, clay, iron, crop - the cost has a 5th entry, the crop upkeep) so it is
    // unit tested without the game types. Costs of candidates are ESTIMATES (BuildingEnums.GetCost); the real cost is read from
    // the build page when the candidate is built, a miss there simply ends the run like any other missing resource.
    public static class FlexibleBuildRules
    {
        public enum Verdict
        {
            // Stock (plus hero) covers the cost and nothing blocks the build.
            Affordable,
            // Resources are missing: waiting would help.
            NotYet,
            // Waiting never helps (cost above the warehouse/granary, not enough free crop for the upkeep).
            Blocked,
        }

        // At most this many jobs are built out of order in one run (each one also fills the build queue, so it is a safety net).
        public const int MaxBypassPerRun = 3;

        // At most this many jobs behind the first one are examined.
        public const int MaxJobsExamined = 40;

        // Look behind the first job only when the feature is on, the Roman queue logic (which already chooses between resource
        // and infrastructure jobs) is off, and the first job's wait is KNOWN (> 0) and longer than the threshold. An unreadable
        // wait (zero) is not a reason to reorder.
        public static bool ShouldLookBehind(bool enabled, bool romanQueueLogic, TimeSpan leadWait, int thresholdMinutes)
        {
            if (!enabled || romanQueueLogic) return false;
            if (leadWait <= TimeSpan.Zero) return false;
            return leadWait > TimeSpan.FromMinutes(Math.Max(thresholdMinutes, 0));
        }

        // cost: wood, clay, iron, crop (+ upkeep as 5th entry when present). stock/hero: wood, clay, iron, crop.
        public static Verdict Judge(long[] cost, long[] stock, long freeCrop, long warehouse, long granary, long[] hero, bool useHero)
        {
            if (cost.Length < 4 || stock.Length < 4) return Verdict.Blocked;

            if (cost[3] > granary) return Verdict.Blocked;
            if (Math.Max(cost[0], Math.Max(cost[1], cost[2])) > warehouse) return Verdict.Blocked;
            if (cost.Length >= 5 && cost[4] > freeCrop) return Verdict.Blocked;

            for (var i = 0; i < 4; i++)
            {
                var have = stock[i] + (useHero && hero.Length >= 4 ? hero[i] : 0);
                if (cost[i] > have) return Verdict.NotYet;
            }
            return Verdict.Affordable;
        }

        // Resource-field job ("all fields to level N"): of the fields still below the target, the affordable ones are looked at
        // and the lowest level wins (lowest location on a tie). canPay(type, nextLevel) is the caller's affordability check.
        // Returns the location, or -1 when no field can be paid for now.
        public static int PickField(IEnumerable<(int Location, int Type, int Level)> fieldsBelowTarget, Func<int, int, bool> canPay)
        {
            var best = -1;
            var bestLevel = int.MaxValue;
            foreach (var (location, type, level) in fieldsBelowTarget.OrderBy(x => x.Level).ThenBy(x => x.Location))
            {
                if (!canPay(type, Math.Max(level, 0) + 1)) continue;
                if (level < bestLevel)
                {
                    best = location;
                    bestLevel = level;
                }
            }
            return best;
        }
    }
}
