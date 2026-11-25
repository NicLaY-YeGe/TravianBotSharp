namespace MainCore.Commands.Features.SendResource
{
    // 2026-11-25 (live log 2026-11-25 00:04-05:58, village 33411): "Overflow to hammer" (start 15 %, drain to 7 %) shipped
    // 44k wood and 56k clay to the hammer while the same village's next building waited five hours for 16870 wood /
    // 13500 clay. Wood stood at 10878 / 10881 / 10878 at three different Upgrade visits = just below the overflow line,
    // i.e. the cost was above the line and could never be reached; once the village was only 871 clay short and
    // 6 s later 5250 clay left. Rule: a village never ships away what its own waiting build job needs.
    //
    //     sendable = min( surplus above the drain target , stock - floor )        floor = cost of the waiting job, capped at capacity
    //
    // The floor comes from ConstructionNeed.GetWaiting (set only while UpgradeBuildingTask waits for resources, expires on
    // its own). It does not depend on the "construction reserve" percent, that one only limits troop training.
    // Arrays are always Wood / Clay / Iron / Crop. Pure, unit tested.
    public static class OutgoingFloorRules
    {
        public static readonly string[] ResourceTypes = ["wood", "clay", "iron", "crop"];

        // Per resource: the waiting job's cost, never above the capacity (Warehouse for wood/clay/iron, Granary for crop).
        // No waiting cost = all zeros (nothing is kept).
        public static long[] Floor(long warehouse, long granary, long[]? waitingCost)
        {
            var floor = new long[4];
            if (waitingCost is not { Length: >= 4 }) return floor;

            long[] capacity = [Math.Max(0, warehouse), Math.Max(0, warehouse), Math.Max(0, warehouse), Math.Max(0, granary)];
            for (var i = 0; i < 4; i++) floor[i] = Math.Min(Math.Max(0, waitingCost[i]), capacity[i]);
            return floor;
        }

        // How much of one resource may leave: the planned surplus, but never into the floor.
        public static long Sendable(long surplus, long stock, long floor)
        {
            return Math.Min(Math.Max(0, surplus), Math.Max(0, stock - Math.Max(0, floor)));
        }

        public static int IndexOf(string resourceType) => Array.IndexOf(ResourceTypes, resourceType);

        // The floor of one resource by name (0 for an unknown name).
        public static long FloorOf(long[] floor, string resourceType)
        {
            var index = IndexOf(resourceType);
            return index < 0 || index >= floor.Length ? 0 : floor[index];
        }
    }
}
