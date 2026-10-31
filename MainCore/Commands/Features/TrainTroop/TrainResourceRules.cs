namespace MainCore.Commands.Features.TrainTroop
{
    // 2026-10-30: when a queue-horizon batch costs more than the village holds, the missing resources are made up
    // in this order (user decision): the village's own stock, then an NPC trade (gold; it only REDISTRIBUTES the
    // village's total), then the hero's inventory (never below the account's reserve). Arrays are always
    // Wood / Clay / Iron / Crop. Pure, unit tested.
    public static class TrainResourceRules
    {
        public static bool HasCost(long[] unitCost)
        {
            if (unitCost is null || unitCost.Length != 4) return false;
            foreach (var value in unitCost)
            {
                if (value > 0) return true;
            }
            return false;
        }

        public static long[] Needed(long[] unitCost, int units)
        {
            var needed = new long[4];
            for (var i = 0; i < 4; i++) needed[i] = unitCost[i] * Math.Max(0, units);
            return needed;
        }

        public static long[] Missing(long[] needed, long[] stock)
        {
            var missing = new long[4];
            for (var i = 0; i < 4; i++) missing[i] = Math.Max(0, needed[i] - stock[i]);
            return missing;
        }

        public static bool AnyMissing(long[] missing)
        {
            foreach (var value in missing)
            {
                if (value > 0) return true;
            }
            return false;
        }

        // What the hero may give: its stock minus the reserve that is kept for Defend / Revive (per resource).
        public static long[] HeroAvailable(long[] heroStock, long reserve)
        {
            var keep = Math.Max(0, reserve);
            var available = new long[4];
            for (var i = 0; i < 4; i++) available[i] = Math.Max(0, heroStock[i] - keep);
            return available;
        }

        // The largest number of units (up to `units`) the village stock plus `extra` can pay for.
        public static int UnitsThatFit(int units, long[] unitCost, long[] stock, long[] extra)
        {
            var fit = Math.Max(0, units);
            for (var i = 0; i < 4; i++)
            {
                if (unitCost[i] <= 0) continue;
                var affordable = (stock[i] + extra[i]) / unitCost[i];
                if (affordable < fit) fit = (int)affordable;
            }
            return Math.Max(0, fit);
        }

        // NPC trade: exact absolute amounts per resource that cover `needed` while keeping the village's TOTAL
        // unchanged (a trade cannot create resources). The deficit is taken from the resources that have a
        // surplus, in proportion to that surplus, so the rest of the mix stays as it was. Null when there is
        // nothing to trade (no deficit) or the surplus cannot cover the deficit.
        public static long[]? NpcTarget(long[] stock, long[] needed)
        {
            var deficit = 0L;
            var surplus = new long[4];
            var totalSurplus = 0L;
            for (var i = 0; i < 4; i++)
            {
                if (needed[i] > stock[i])
                {
                    deficit += needed[i] - stock[i];
                }
                else
                {
                    surplus[i] = stock[i] - needed[i];
                    totalSurplus += surplus[i];
                }
            }

            if (deficit <= 0) return null;
            if (totalSurplus < deficit) return null;

            var target = new long[4];
            var taken = 0L;
            for (var i = 0; i < 4; i++)
            {
                if (needed[i] > stock[i])
                {
                    target[i] = needed[i];
                    continue;
                }

                var take = deficit * surplus[i] / totalSurplus;
                target[i] = stock[i] - take;
                taken += take;
            }

            // The integer division can leave a few units untaken: take them from the resource with the most room.
            var rest = deficit - taken;
            while (rest > 0)
            {
                var index = -1;
                var room = 0L;
                for (var i = 0; i < 4; i++)
                {
                    if (needed[i] > stock[i]) continue;
                    var spare = target[i] - needed[i];
                    if (spare > room)
                    {
                        room = spare;
                        index = i;
                    }
                }

                if (index < 0) break;
                var take = Math.Min(rest, room);
                target[index] -= take;
                rest -= take;
            }

            return target;
        }
    }
}
