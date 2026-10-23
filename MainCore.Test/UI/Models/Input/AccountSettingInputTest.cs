using MainCore.Enums;
using MainCore.UI.Models.Input;

namespace MainCore.Test.UI.Models.Input
{
    public class AccountSettingInputTest
    {
        [Fact]
        public void Get_ReturnsDictionaryWithCorrectCount()
        {
            // Arrange
            var accountSettingInput = new AccountSettingInput();

            // Act
            var result = accountSettingInput.Get();

            // Assert
            // Internal bookkeeping values written by the bot itself (no UI field, so they are
            // not part of AccountSettingInput) - see the comments in AccountSettingEnums.
            var internalOnly = new[]
            {
                AccountSettingEnums.RaidListNextAllowedSendAtMinutes,
                AccountSettingEnums.RaidListNextAllowedSendAtSeconds,
                AccountSettingEnums.RaidReportLastId,
                AccountSettingEnums.ScoutReportLastId,
            };
            var enumCount = Enum.GetValues(typeof(AccountSettingEnums)).Length - internalOnly.Length;
            Assert.Equal(enumCount, result.Count);
            Assert.DoesNotContain(result.Keys, key => internalOnly.Contains(key));
        }
    }
}