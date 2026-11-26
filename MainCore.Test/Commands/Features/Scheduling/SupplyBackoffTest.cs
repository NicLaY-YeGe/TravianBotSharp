using MainCore.Commands.Features.Scheduling;
using MainCore.Entities;

namespace MainCore.Test.Commands.Features.Scheduling
{
    public class SupplyBackoffTest
    {
        private static readonly DateTime Now = new(2026, 11, 2, 12, 0, 0);

        [Fact]
        public void Block_StopsAskingUntilItExpires()
        {
            var source = new VillageId(910001);
            var target = new VillageId(910002);

            SupplyBackoff.IsBlocked(source, target, Now).ShouldBeFalse();

            SupplyBackoff.Block(source, target, Now.AddMinutes(10));
            SupplyBackoff.IsBlocked(source, target, Now.AddMinutes(9)).ShouldBeTrue();
            SupplyBackoff.IsBlocked(source, target, Now.AddMinutes(10)).ShouldBeFalse();

            SupplyBackoff.Clear(source, target);
        }

        [Fact]
        public void Block_IsPerSourceAndTarget()
        {
            var source = new VillageId(910011);
            var target = new VillageId(910012);
            var other = new VillageId(910013);

            SupplyBackoff.Block(source, target, Now.AddMinutes(10));
            SupplyBackoff.IsBlocked(source, other, Now).ShouldBeFalse();
            SupplyBackoff.IsBlocked(other, target, Now).ShouldBeFalse();

            SupplyBackoff.Clear(source, target);
        }

        [Fact]
        public void Seconds_AreEightToTwelveMinutes()
        {
            for (var i = 0; i < 200; i++)
            {
                var value = SupplyBackoff.Seconds(new Random(i));
                (value >= 480 && value <= 720).ShouldBeTrue();
            }
        }
    }
}
