using MainCore.Commands.Features.RaidListScheduling;

namespace MainCore.Test.Commands.Features.RaidListScheduling
{
    public class RaidListDeferPlannerTest
    {
        private static readonly DateTime Gate = new(2026, 9, 20, 20, 52, 53);

        [Fact]
        public void NextSlot_EmptyQueue_ReturnsGateTime()
        {
            RaidListDeferPlanner.NextSlot(Gate, null, 26, 300, 500, new Random(1)).ShouldBe(Gate);
        }

        [Fact]
        public void NextSlot_TailBehindGate_ReturnsGateTime()
        {
            var oldTail = Gate.AddSeconds(-60);
            RaidListDeferPlanner.NextSlot(Gate, oldTail, 26, 300, 500, new Random(1)).ShouldBe(Gate);
        }

        [Fact]
        public void NextSlot_FollowsTailWithinGapRange()
        {
            var tail = Gate.AddSeconds(600);
            var slot = RaidListDeferPlanner.NextSlot(Gate, tail, 26, 300, 500, new Random(7));

            var gap = (slot - tail).TotalSeconds;
            gap.ShouldBeGreaterThanOrEqualTo(300);
            gap.ShouldBeLessThanOrEqualTo(500);
        }

        [Fact]
        public void NextSlot_ManyDeferredRows_GetDistinctIncreasingSlots()
        {
            var random = new Random(3);
            DateTime? last = null;
            var slots = new List<DateTime>();

            for (var i = 0; i < 25; i++)
            {
                var slot = RaidListDeferPlanner.NextSlot(Gate, last, 26, 300, 500, random);
                slots.Add(slot);
                last = slot;
            }

            slots[0].ShouldBe(Gate);
            slots.Distinct().Count().ShouldBe(25);
            for (var i = 1; i < slots.Count; i++)
            {
                (slots[i] - slots[i - 1]).TotalSeconds.ShouldBeGreaterThanOrEqualTo(300);
            }
        }

        [Fact]
        public void NextSlot_TailLongerThanAnyRealQueue_StartsOver()
        {
            // 2 active rows can queue at most 2 * 500 s past the gate; a tail 3 h out is stale.
            var staleTail = Gate.AddHours(3);
            RaidListDeferPlanner.NextSlot(Gate, staleTail, 2, 300, 500, new Random(1)).ShouldBe(Gate);
        }

        [Fact]
        public void NextSlot_DegenerateSettings_AreClamped()
        {
            var tail = Gate.AddSeconds(10);
            // min > max and min < 1 must not throw; behaves like a 1-second gap range.
            var slot = RaidListDeferPlanner.NextSlot(Gate, tail, 50, 0, 0, new Random(1));
            (slot - tail).TotalSeconds.ShouldBe(1);
        }
    }
}
