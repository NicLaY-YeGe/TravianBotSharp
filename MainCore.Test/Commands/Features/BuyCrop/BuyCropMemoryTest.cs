using MainCore.Commands.Features.BuyCrop;

namespace MainCore.Test.Commands.Features.BuyCrop
{
    public class BuyCropMemoryTest
    {
        private static readonly DateTime Now = new(2026, 11, 10, 12, 0, 0);

        // every test uses its own village id: the memory is static
        [Fact]
        public void Pending_CountsUntilTheCropArrives()
        {
            const int village = 910001;
            BuyCropMemory.AddPending(village, 2400, Now.AddHours(2));
            BuyCropMemory.AddPending(village, 1200, Now.AddMinutes(30));

            BuyCropMemory.PendingAmount(village, Now).ShouldBe(3600);
            BuyCropMemory.PendingAmount(village, Now.AddMinutes(31)).ShouldBe(2400);
            BuyCropMemory.PendingAmount(village, Now.AddHours(3)).ShouldBe(0);
        }

        [Fact]
        public void Pending_IgnoresEmptyAmountsAndUnknownVillages()
        {
            const int village = 910002;
            BuyCropMemory.AddPending(village, 0, Now.AddHours(1));
            BuyCropMemory.PendingAmount(village, Now).ShouldBe(0);
            BuyCropMemory.PendingAmount(910099, Now).ShouldBe(0);
        }

        [Fact]
        public void NextCheck_HoldsTheTaskBackUntilItIsDue()
        {
            const int village = 910003;
            BuyCropMemory.IsDue(village, Now).ShouldBeTrue();

            BuyCropMemory.SetNextCheck(village, Now.AddMinutes(15));
            BuyCropMemory.IsDue(village, Now).ShouldBeFalse();
            BuyCropMemory.IsDue(village, Now.AddMinutes(15)).ShouldBeTrue();
        }

        [Fact]
        public void Clear_ForgetsEverything()
        {
            const int village = 910004;
            BuyCropMemory.AddPending(village, 1200, Now.AddHours(1));
            BuyCropMemory.SetNextCheck(village, Now.AddHours(1));

            BuyCropMemory.Clear(village);

            BuyCropMemory.PendingAmount(village, Now).ShouldBe(0);
            BuyCropMemory.IsDue(village, Now).ShouldBeTrue();
        }
    }
}
