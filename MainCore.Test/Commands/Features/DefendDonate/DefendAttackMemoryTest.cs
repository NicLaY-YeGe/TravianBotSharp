using MainCore.Commands.Features.DefendDonate;

namespace MainCore.Test.Commands.Features.DefendDonate
{
    public class DefendAttackMemoryTest
    {
        private static readonly DateTime Now = new(2026, 10, 19, 16, 18, 0);

        [Fact]
        public void NotMarked_IsNotHandled()
        {
            DefendAttackMemory.Clear();
            DefendAttackMemory.IsHandled(1, Now).ShouldBeFalse();
        }

        [Fact]
        public void Marked_IsHandledUntilExpiry()
        {
            DefendAttackMemory.Clear();
            DefendAttackMemory.MarkHandled(1, Now.AddSeconds(180), Now);

            DefendAttackMemory.IsHandled(1, Now.AddSeconds(60)).ShouldBeTrue();
            DefendAttackMemory.IsHandled(1, Now.AddSeconds(181)).ShouldBeFalse();
        }

        [Fact]
        public void Marked_OtherVillageUnaffected()
        {
            DefendAttackMemory.Clear();
            DefendAttackMemory.MarkHandled(1, Now.AddSeconds(180), Now);

            DefendAttackMemory.IsHandled(2, Now).ShouldBeFalse();
        }

        [Fact]
        public void MarkHandled_DropsExpiredEntries()
        {
            DefendAttackMemory.Clear();
            DefendAttackMemory.MarkHandled(1, Now.AddSeconds(10), Now);
            DefendAttackMemory.MarkHandled(2, Now.AddSeconds(500), Now.AddSeconds(100));

            DefendAttackMemory.IsHandled(1, Now.AddSeconds(100)).ShouldBeFalse();
            DefendAttackMemory.IsHandled(2, Now.AddSeconds(100)).ShouldBeTrue();
        }
    }
}
