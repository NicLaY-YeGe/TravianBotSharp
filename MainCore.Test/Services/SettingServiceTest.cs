using MainCore.Entities;
using MainCore.Enums;
using MainCore.Infrasturecture.Persistence;
using MainCore.Services;

namespace MainCore.Test.Services
{
    public class SettingServiceTest
    {
        private static void SetMask(AppDbContext context, int accountId, int mask)
        {
            context.Add(new AccountSetting
            {
                AccountId = accountId,
                Setting = AccountSettingEnums.OnlineHoursMask,
                Value = mask,
            });
            context.SaveChanges();
        }

        [Fact]
        public void IsCurrentHourOnline_AllHoursMask_ReturnsTrue()
        {
            // Arrange
            using var context = new FakeDbContextFactory().CreateDbContext(true);
            SetMask(context, 1, AppDbContext.OnlineHoursMaskAll);
            var service = new SettingService(context);

            // Act
            var result = service.IsCurrentHourOnline(new AccountId(1));

            // Assert
            result.ShouldBeTrue();
        }

        [Fact]
        public void IsCurrentHourOnline_EmptyMask_ReturnsFalse()
        {
            // Arrange
            using var context = new FakeDbContextFactory().CreateDbContext(true);
            SetMask(context, 1, 0);
            var service = new SettingService(context);

            // Act
            var result = service.IsCurrentHourOnline(new AccountId(1));

            // Assert
            result.ShouldBeFalse();
        }

        [Fact]
        public void IsCurrentHourOnline_OnlyCurrentHourBitSet_ReturnsTrue()
        {
            // Arrange
            using var context = new FakeDbContextFactory().CreateDbContext(true);
            var currentHourMask = 1 << DateTime.Now.Hour;
            SetMask(context, 1, currentHourMask);
            var service = new SettingService(context);

            // Act
            var result = service.IsCurrentHourOnline(new AccountId(1));

            // Assert
            result.ShouldBeTrue();
        }

        [Fact]
        public void IsCurrentHourOnline_CurrentHourBitCleared_ReturnsFalse()
        {
            // Arrange
            using var context = new FakeDbContextFactory().CreateDbContext(true);
            var maskWithoutCurrentHour = AppDbContext.OnlineHoursMaskAll & ~(1 << DateTime.Now.Hour);
            SetMask(context, 1, maskWithoutCurrentHour);
            var service = new SettingService(context);

            // Act
            var result = service.IsCurrentHourOnline(new AccountId(1));

            // Assert
            result.ShouldBeFalse();
        }
    }
}
