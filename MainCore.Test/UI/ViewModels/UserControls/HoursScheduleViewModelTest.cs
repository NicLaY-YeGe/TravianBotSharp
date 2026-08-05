using MainCore.UI.ViewModels.UserControls;

namespace MainCore.Test.UI.ViewModels.UserControls
{
    public class HoursScheduleViewModelTest
    {
        [Fact]
        public void SetThenGet_RoundTripsMask()
        {
            // Arrange
            var vm = new HoursScheduleViewModel();
            var mask = (1 << 0) | (1 << 7) | (1 << 23); // hours 0, 7, 23

            // Act
            vm.Set(mask);
            var result = vm.Get();

            // Assert
            result.ShouldBe(mask);
        }

        [Fact]
        public void Set_ChecksExactlyTheMatchingHours()
        {
            // Arrange
            var vm = new HoursScheduleViewModel();
            var mask = (1 << 3) | (1 << 14);

            // Act
            vm.Set(mask);

            // Assert
            vm.Hours.Single(x => x.Hour == 3).IsChecked.ShouldBeTrue();
            vm.Hours.Single(x => x.Hour == 14).IsChecked.ShouldBeTrue();
            vm.Hours.Count(x => x.IsChecked).ShouldBe(2);
        }

        [Fact]
        public void SelectAll_SetsAllTwentyFourHours()
        {
            // Arrange
            var vm = new HoursScheduleViewModel();

            // Act
            vm.SelectAll();

            // Assert
            vm.Hours.Count.ShouldBe(24);
            vm.Get().ShouldBe(MainCore.Infrasturecture.Persistence.AppDbContext.OnlineHoursMaskAll);
        }

        [Fact]
        public void SelectNone_ClearsMaskToZero()
        {
            // Arrange
            var vm = new HoursScheduleViewModel();
            vm.SelectAll();

            // Act
            vm.SelectNone();

            // Assert
            vm.Get().ShouldBe(0);
        }
    }
}
