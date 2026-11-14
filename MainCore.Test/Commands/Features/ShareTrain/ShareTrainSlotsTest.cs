using MainCore.Commands.Features.ShareTrain;
using MainCore.Commands.Features.TrainTroop;
using MainCore.Enums;

namespace MainCore.Test.Commands.Features.ShareTrain
{
    public class ShareTrainSlotsTest
    {
        [Fact]
        public void EveryTrainingSlotHasItsOwnWeightKey()
        {
            var keys = ShareTrainSlots.AllKeys().ToList();

            keys.Count.ShouldBe(10);
            keys.Distinct().Count().ShouldBe(10);
            keys.Count.ShouldBe(ShareTrainSlots.Buildings.Sum(TrainTroopSlots.SlotCount));
        }

        [Fact]
        public void GetWeights_ListsOnlyTroopsWithAWeight()
        {
            var settings = new Dictionary<VillageSettingEnums, int>
            {
                [ShareTrainSlots.Key(BuildingEnums.Barracks, 1)] = 5,
                [ShareTrainSlots.Key(BuildingEnums.Barracks, 2)] = 0,
                [ShareTrainSlots.Key(BuildingEnums.Stable, 3)] = 2,
            };

            var weights = ShareTrainSlots.GetWeights(TribeEnums.Gauls, key => settings.GetValueOrDefault(key));

            weights.Count.ShouldBe(2);
            weights[0].Building.ShouldBe(BuildingEnums.Barracks);
            weights[0].Slot.ShouldBe(1);
            weights[0].Weight.ShouldBe(5);
            weights[1].Building.ShouldBe(BuildingEnums.Stable);
            weights[1].Slot.ShouldBe(3);
        }

        [Fact]
        public void GetWeights_ClampsToTheMaximum()
        {
            var weights = ShareTrainSlots.GetWeights(TribeEnums.Gauls, key => key == ShareTrainSlots.Key(BuildingEnums.Barracks, 1) ? 99999 : 0);

            weights.Single().Weight.ShouldBe(ShareTrainRules.MaxWeight);
        }
    }
}
