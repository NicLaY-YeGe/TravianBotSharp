using MainCore.Commands.Features.SendResource;

namespace MainCore.Test.Commands.Features.SendResource
{
    public class OutgoingFloorRulesTest
    {
        [Fact]
        public void Floor_NoWaitingCost_IsAllZero()
        {
            string.Join(",", OutgoingFloorRules.Floor(70000, 70000, null)).ShouldBe("0,0,0,0");
            string.Join(",", OutgoingFloorRules.Floor(70000, 70000, new long[] { 1, 2 })).ShouldBe("0,0,0,0");
        }

        [Fact]
        public void Floor_IsTheCost_CappedAtCapacity()
        {
            // wood/clay/iron use the Warehouse, crop the Granary; a cost above the capacity can never be reached.
            string.Join(",", OutgoingFloorRules.Floor(15000, 8000, new long[] { 16870, 13500, 0, 9000 })).ShouldBe("15000,13500,0,8000");
        }

        [Fact]
        public void Floor_NegativeCostIsIgnored()
        {
            string.Join(",", OutgoingFloorRules.Floor(70000, 70000, new long[] { -5, 0, 10, 0 })).ShouldBe("0,0,10,0");
        }

        [Theory]
        [InlineData(5000, 12000, 0, 5000)]      // no floor: the planned surplus
        [InlineData(5000, 12000, 10000, 2000)]  // floor cuts the surplus (stock 12000 - 10000)
        [InlineData(5000, 12000, 12000, 0)]     // stock exactly at the floor: nothing may leave
        [InlineData(5000, 8000, 10000, 0)]      // stock below the floor: nothing may leave, never negative
        [InlineData(0, 12000, 0, 0)]            // no surplus
        [InlineData(-3, 12000, 0, 0)]
        [InlineData(5000, 12000, -7, 5000)]     // a negative floor is ignored
        public void Sendable_NeverGoesIntoTheFloor(long surplus, long stock, long floor, long expected)
        {
            OutgoingFloorRules.Sendable(surplus, stock, floor).ShouldBe(expected);
        }

        [Fact]
        public void LiveCase_Village33411_ClayJobShortBy871_KeepsItsClay()
        {
            // 2026-11-25 05:00:45: clay 12629 of 13500 needed; the next shipment must not take any.
            OutgoingFloorRules.Sendable(5250, 12629, 13500).ShouldBe(0);
        }

        [Theory]
        [InlineData("wood", 0)]
        [InlineData("clay", 1)]
        [InlineData("iron", 2)]
        [InlineData("crop", 3)]
        [InlineData("gold", -1)]
        public void IndexOf_FollowsWoodClayIronCrop(string name, int expected)
        {
            OutgoingFloorRules.IndexOf(name).ShouldBe(expected);
        }

        [Fact]
        public void FloorOf_ReadsByName_AndIsZeroForUnknown()
        {
            long[] floor = [10, 20, 30, 40];
            OutgoingFloorRules.FloorOf(floor, "iron").ShouldBe(30);
            OutgoingFloorRules.FloorOf(floor, "gold").ShouldBe(0);
        }
    }
}
