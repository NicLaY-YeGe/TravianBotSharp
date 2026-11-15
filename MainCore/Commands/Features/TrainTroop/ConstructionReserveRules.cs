namespace MainCore.Commands.Features.TrainTroop
{
    // 2026-11-07, user decision ("construction reserve"): troop training must not eat the resources the build
    // plan needs. The village setting ConstructionReservePercent (0 = off) defines a FLOOR per resource:
    //
    //     floor = max( percent of the storage capacity , cost of the next building )      (never above the capacity)
    //
    // Training may only spend the part of the stock that lies ABOVE the floor; construction may spend everything.
    // Capacity = Warehouse for wood / clay / iron and Granary for crop. Arrays are always Wood / Clay / Iron / Crop.
    //
    // It is a floor, not a production split: it guarantees that construction is never starved, it does not
    // promise a fixed share of the spending.
    //
    // "Slot busy" relax (user decision): while the game's build queue still runs for more than RelaxMinutes, the
    // next building cannot start soon, so only the percent part of the floor holds; the cost of the next building
    // is reserved again once the queue has RelaxMinutes or less left. Pure, unit tested.
    public static class ConstructionReserveRules
    {
        public const int RelaxMinutes = 30;
        public const int MaxPercent = 100;

        public static long[] Floor(long warehouse, long granary, int percent, long[]? nextCost, TimeSpan? slotBusyFor)
        {
            var floor = new long[4];
            if (percent <= 0) return floor;

            var clamped = Math.Min(percent, MaxPercent);
            long[] capacity = [Math.Max(0, warehouse), Math.Max(0, warehouse), Math.Max(0, warehouse), Math.Max(0, granary)];

            var useCost = nextCost is { Length: >= 4 } && !(slotBusyFor is { } busy && busy > TimeSpan.FromMinutes(RelaxMinutes));

            for (var i = 0; i < 4; i++)
            {
                var value = capacity[i] * clamped / 100;
                if (useCost) value = Math.Max(value, Math.Max(0, nextCost![i]));
                floor[i] = Math.Min(value, capacity[i]);
            }

            return floor;
        }

        // What training may spend: the stock above the floor (per resource).
        public static long[] Spendable(long[] stock, long[] floor)
        {
            var spendable = new long[4];
            for (var i = 0; i < 4; i++) spendable[i] = Math.Max(0, stock[i] - Math.Max(0, floor[i]));
            return spendable;
        }

        // The largest number of units (up to `wanted`) the spendable stock plus `extra` (e.g. hero resources) can pay.
        public static int UnitsAllowed(int wanted, long[] unitCost, long[] stock, long[] floor, long[]? extra = null)
        {
            return TrainResourceRules.UnitsThatFit(wanted, unitCost, Spendable(stock, floor), extra ?? new long[4]);
        }
    }
}
