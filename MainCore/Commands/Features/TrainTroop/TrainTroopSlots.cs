using MainCore.UI.ViewModels.UserControls;

namespace MainCore.Commands.Features.TrainTroop
{
    // One troop of a training building, as configured by the user.
    // Slot = 1-based position in the building's troop list for the tribe (0 = not a slot setting).
    // AmountMin/Max = units per batch; RepeatMin/Max = this troop's own "not again before" interval in
    // minutes (0/0 = none: the building's old interval, then the general one).
    public sealed record TroopPlan(int Slot, TroopEnums Troop, int AmountMin, int AmountMax, int RepeatMin, int RepeatMax);

    // 2026-10-27, user decision: every troop has its own amount range and interval. The settings are
    // enum-keyed rows, so a troop is addressed by its SLOT (Nth troop of the building for the tribe):
    // Barracks = infantry slots 1-4, Stable = cavalry slots 1-4, Workshop = siege slots 1-2
    // (the largest lists over all tribes). Great Barracks/Great Stable keep the old single-troop settings.
    public static class TrainTroopSlots
    {
        public static int SlotCount(BuildingEnums building) => building switch
        {
            BuildingEnums.Barracks => 4,
            BuildingEnums.Stable => 4,
            BuildingEnums.Workshop => 2,
            _ => 0,
        };

        public static bool IsPerTroop(BuildingEnums building) => SlotCount(building) > 0;

        private static string Prefix(BuildingEnums building) => building switch
        {
            BuildingEnums.Barracks => "Infantry",
            BuildingEnums.Stable => "Cavalry",
            BuildingEnums.Workshop => "Siege",
            _ => throw new ArgumentOutOfRangeException(nameof(building), building, "No per-troop slots"),
        };

        public static (VillageSettingEnums AmountMin, VillageSettingEnums AmountMax, VillageSettingEnums RepeatMin, VillageSettingEnums RepeatMax) Keys(BuildingEnums building, int slot)
        {
            if (slot < 1 || slot > SlotCount(building)) throw new ArgumentOutOfRangeException(nameof(slot), slot, "Slot out of range");
            var name = $"Train{Prefix(building)}{slot}";
            return (
                Enum.Parse<VillageSettingEnums>(name + "AmountMin"),
                Enum.Parse<VillageSettingEnums>(name + "AmountMax"),
                Enum.Parse<VillageSettingEnums>(name + "RepeatMin"),
                Enum.Parse<VillageSettingEnums>(name + "RepeatMax"));
        }

        public static IEnumerable<VillageSettingEnums> AllKeys()
        {
            foreach (var building in new[] { BuildingEnums.Barracks, BuildingEnums.Stable, BuildingEnums.Workshop })
            {
                for (var slot = 1; slot <= SlotCount(building); slot++)
                {
                    var (a, b, c, d) = Keys(building, slot);
                    yield return a;
                    yield return b;
                    yield return c;
                    yield return d;
                }
            }
        }

        // The troops a building can train for the tribe, in slot order.
        public static List<TroopEnums> Troops(BuildingEnums building, TribeEnums tribe)
        {
            var troops = TroopSelectorViewModel.GetTroops(building, tribe);
            var count = IsPerTroop(building) ? SlotCount(building) : troops.Count;
            return troops.Take(count).ToList();
        }
    }

    // Decides which troops a building trains, from the stored settings. Pure so it is unit tested.
    public static class TrainTroopPlanner
    {
        public static List<TroopPlan> GetPlans(BuildingEnums building, TribeEnums tribe, Func<VillageSettingEnums, int> get)
        {
            var plans = new List<TroopPlan>();

            if (TrainTroopSlots.IsPerTroop(building))
            {
                var troops = TrainTroopSlots.Troops(building, tribe);
                for (var i = 0; i < troops.Count; i++)
                {
                    var (aMin, aMax, rMin, rMax) = TrainTroopSlots.Keys(building, i + 1);
                    var amountMin = get(aMin);
                    var amountMax = get(aMax);
                    if (amountMax <= 0 && amountMin <= 0) continue;
                    plans.Add(Normalize(i + 1, troops[i], amountMin, amountMax, get(rMin), get(rMax)));
                }

                if (plans.Count > 0) return plans;
            }

            // Old single-troop setting: the Great Barracks/Great Stable always, the others only for a config
            // saved before per-troop settings existed (saving the village settings clears it).
            if (!TrainTroopCommand.TroopSettings.TryGetValue(building, out var troopKey)) return plans;
            var legacyTroop = (TroopEnums)get(troopKey);
            if (legacyTroop == TroopEnums.None) return plans;

            var (amountMinKey, amountMaxKey) = TrainTroopCommand.AmountSettings[building];
            var (repeatMinKey, repeatMaxKey) = TrainTroopCommand.RepeatSettings[building];
            var slot = TrainTroopSlots.Troops(building, tribe).IndexOf(legacyTroop) + 1;
            plans.Add(Normalize(slot, legacyTroop, get(amountMinKey), get(amountMaxKey), get(repeatMinKey), get(repeatMaxKey)));
            return plans;
        }

        private static TroopPlan Normalize(int slot, TroopEnums troop, int amountMin, int amountMax, int repeatMin, int repeatMax)
        {
            if (amountMax < amountMin) (amountMin, amountMax) = (amountMax, amountMin);
            if (repeatMax < repeatMin) (repeatMin, repeatMax) = (repeatMax, repeatMin);
            return new TroopPlan(slot, troop, Math.Max(0, amountMin), Math.Max(0, amountMax), Math.Max(0, repeatMin), Math.Max(0, repeatMax));
        }

        // Units for one batch: random between min and max (at least 1).
        public static int PickAmount(TroopPlan plan, Random random)
        {
            var min = Math.Max(1, plan.AmountMin);
            var max = Math.Max(min, plan.AmountMax);
            return random.Next(min, max + 1);
        }

        // Interval in seconds: the troop's own range; when it is 0/0 the fallback range (minutes) is used.
        // Never below 60 s.
        public static int IntervalSeconds(TroopPlan plan, int fallbackMinMinutes, int fallbackMaxMinutes, Random random)
        {
            var min = plan.RepeatMin;
            var max = plan.RepeatMax;
            if (min <= 0 && max <= 0)
            {
                min = fallbackMinMinutes;
                max = fallbackMaxMinutes;
            }

            if (max < min) (min, max) = (max, min);
            var seconds = random.Next(Math.Max(0, min) * 60, Math.Max(0, max) * 60 + 1);
            return Math.Max(60, seconds);
        }
    }
}
