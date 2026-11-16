using MainCore.Commands.Features.TrainTroop;

namespace MainCore.Commands.Features.ShareTrain
{
    // One troop of the share mode: the building and slot it belongs to (TrainTroopSlots, tribe relative) and its weight.
    public sealed record ShareWeight(BuildingEnums Building, int Slot, TroopEnums Troop, int Weight);

    // Weights live in the village settings as TrainShare<Infantry|Cavalry|Siege><slot> (Barracks 1-4, Stable 1-4,
    // Workshop 1-2 = the same 10 slots as TrainTroopSlots). Great Barracks / Great Stable are not part of this mode.
    public static class ShareTrainSlots
    {
        public static readonly BuildingEnums[] Buildings = [BuildingEnums.Barracks, BuildingEnums.Stable, BuildingEnums.Workshop];

        private static string Prefix(BuildingEnums building) => building switch
        {
            BuildingEnums.Barracks => "Infantry",
            BuildingEnums.Stable => "Cavalry",
            BuildingEnums.Workshop => "Siege",
            _ => throw new ArgumentOutOfRangeException(nameof(building), building, "No share slots"),
        };

        public static VillageSettingEnums Key(BuildingEnums building, int slot)
        {
            if (slot < 1 || slot > TrainTroopSlots.SlotCount(building)) throw new ArgumentOutOfRangeException(nameof(slot), slot, "Slot out of range");
            return Enum.Parse<VillageSettingEnums>($"TrainShare{Prefix(building)}{slot}");
        }

        public static IEnumerable<VillageSettingEnums> AllKeys()
        {
            foreach (var building in Buildings)
            {
                for (var slot = 1; slot <= TrainTroopSlots.SlotCount(building); slot++) yield return Key(building, slot);
            }
        }

        // Every troop of the tribe with a weight above 0, building by building in slot order.
        public static List<ShareWeight> GetWeights(TribeEnums tribe, Func<VillageSettingEnums, int> get)
        {
            var list = new List<ShareWeight>();
            foreach (var building in Buildings)
            {
                var troops = TrainTroopSlots.Troops(building, tribe);
                for (var i = 0; i < troops.Count; i++)
                {
                    var weight = Math.Clamp(get(Key(building, i + 1)), 0, ShareTrainRules.MaxWeight);
                    if (weight > 0) list.Add(new ShareWeight(building, i + 1, troops[i], weight));
                }
            }
            return list;
        }
    }
}
