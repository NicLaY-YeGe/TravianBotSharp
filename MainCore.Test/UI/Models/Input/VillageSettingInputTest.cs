using MainCore.Enums;
using MainCore.UI.Models.Input;

namespace MainCore.Test.UI.Models.Input
{
    public class VillageSettingInputTest
    {
        [Fact]
        public void Get_ReturnsDictionaryWithCorrectCount()
        {
            // Arrange
            var villageSettingInput = new VillageSettingInput();

            // Act
            var result = villageSettingInput.Get();

            // Assert
            // Internal bookkeeping values written by the bot itself (no UI field, so they are
            // not part of VillageSettingInput) - see the comments in VillageSettingEnums.
            var internalOnly = new[]
            {
                VillageSettingEnums.DodgeTroopSlot,
                VillageSettingEnums.SmithyUpgradeBusyUntilUnixTime,
                VillageSettingEnums.SmallCelebrationBusyUntilUnixTime,
                VillageSettingEnums.NeedExpansionWood,
                VillageSettingEnums.NeedExpansionClay,
                VillageSettingEnums.NeedExpansionIron,
                VillageSettingEnums.NeedExpansionCrop,
                VillageSettingEnums.NeedReviveWood,
                VillageSettingEnums.NeedReviveClay,
                VillageSettingEnums.NeedReviveIron,
                VillageSettingEnums.NeedReviveCrop,
                VillageSettingEnums.ReviveWaitingCycles,
            };
            var enumCount = Enum.GetValues(typeof(VillageSettingEnums)).Length - internalOnly.Length;
            Assert.Equal(enumCount, result.Count);
            Assert.DoesNotContain(result.Keys, key => internalOnly.Contains(key));
        }
    }
}