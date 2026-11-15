using MainCore.Commands.Features.ShareTrain;

namespace MainCore.Test.Commands.Features.ShareTrain
{
    public class ShareTrainMemoryTest
    {
        private static readonly DateTime Now = new(2026, 11, 13, 12, 0, 0);

        // every test uses its own village id: the memory is static
        [Fact]
        public void NextCheck_HoldsTheTaskBackUntilItIsDue()
        {
            const int village = 920001;
            ShareTrainMemory.IsDue(village, Now).ShouldBeTrue();

            ShareTrainMemory.SetNextCheck(village, Now.AddMinutes(4));
            ShareTrainMemory.IsDue(village, Now).ShouldBeFalse();
            ShareTrainMemory.IsDue(village, Now.AddMinutes(4)).ShouldBeTrue();
        }

        [Fact]
        public void UnitCost_IsKnownUntilItExpires()
        {
            const int village = 920003;
            ShareTrainMemory.GetUnitCost(village, 1, Now).ShouldBeNull();

            ShareTrainMemory.SetUnitCost(village, 1, [100, 130, 55, 30], Now);
            string.Join("/", ShareTrainMemory.GetUnitCost(village, 1, Now.AddHours(11))!).ShouldBe("100/130/55/30");
            ShareTrainMemory.GetUnitCost(village, 1, Now.AddHours(12)).ShouldBeNull();
        }

        [Fact]
        public void Unavailable_IsAnEmptyCostForOneHour()
        {
            const int village = 920004;
            ShareTrainMemory.SetUnavailable(village, 2, Now);

            ShareTrainMemory.GetUnitCost(village, 2, Now.AddMinutes(59))!.Length.ShouldBe(0);
            ShareTrainMemory.GetUnitCost(village, 2, Now.AddHours(1)).ShouldBeNull();
        }

        [Fact]
        public void Clear_ForgetsTheHold()
        {
            const int village = 920002;
            ShareTrainMemory.SetNextCheck(village, Now.AddHours(1));
            ShareTrainMemory.Clear(village);

            ShareTrainMemory.IsDue(village, Now).ShouldBeTrue();
        }
    }
}
