using MainCore.Commands.Features.SendResource;

namespace MainCore.Test.Commands.Features.SendResource
{
    public class ReceiveRoomRulesTest
    {
        [Fact]
        public void CeilingPercent_IsTheLowestEnabledOutgoingThreshold()
        {
            ReceiveRoomRules.CeilingPercent(false, 90, false, 90).ShouldBe(100);
            ReceiveRoomRules.CeilingPercent(true, 90, false, 70).ShouldBe(90);
            ReceiveRoomRules.CeilingPercent(false, 90, true, 70).ShouldBe(70);
            ReceiveRoomRules.CeilingPercent(true, 90, true, 70).ShouldBe(70);
            ReceiveRoomRules.CeilingPercent(true, 60, true, 80).ShouldBe(60);
        }

        [Fact]
        public void CeilingPercent_ClampsOddValues()
        {
            ReceiveRoomRules.CeilingPercent(true, 150, false, 0).ShouldBe(100);
            ReceiveRoomRules.CeilingPercent(true, 0, false, 0).ShouldBe(0);
            ReceiveRoomRules.CeilingPercent(true, -5, false, 0).ShouldBe(0);
        }

        [Fact]
        public void Room_WithoutALimitIsFreeSpace()
        {
            ReceiveRoomRules.Room(10000, 2500, 100).ShouldBe(7500);
            ReceiveRoomRules.Room(10000, 10000, 100).ShouldBe(0);
            ReceiveRoomRules.Room(10000, 12000, 100).ShouldBe(0);
        }

        [Fact]
        public void Room_StopsStrictlyBelowTheOutgoingThreshold()
        {
            // 90 % of 10000 = 9000: stock 9000 would already send away, 8999 would not.
            ReceiveRoomRules.Room(10000, 5000, 90).ShouldBe(3999);
            ReceiveRoomRules.Room(10000, 8999, 90).ShouldBe(0);
            ReceiveRoomRules.Room(10000, 9000, 90).ShouldBe(0);
            ReceiveRoomRules.Room(10000, 9500, 90).ShouldBe(0);
        }

        [Fact]
        public void Room_HandlesCapacitiesThatDoNotDivideEvenly()
        {
            // 90 % of 12345 = 11110.5 -> 11110 is still quiet (11110 * 100 / 12345 = 89.99 %), 11111 is not.
            ReceiveRoomRules.Room(12345, 0, 90).ShouldBe(11110);
            ReceiveRoomRules.Room(12345, 11110, 90).ShouldBe(0);
        }

        [Fact]
        public void Room_ZeroCeilingOrNoCapacityMeansNoRoom()
        {
            ReceiveRoomRules.Room(10000, 0, 0).ShouldBe(0);
            ReceiveRoomRules.Room(0, 0, 90).ShouldBe(0);
            ReceiveRoomRules.Room(-1, 0, 100).ShouldBe(0);
        }

        [Fact]
        public void Room_RealLogCase_ReceiverNearItsThresholdTakesNothing()
        {
            // 2026-11-15 log: a granary of 80000 with an outgoing threshold of 90 % (72000) that already holds 70000
            // may take 1999 more, not the 10000 that "capacity - stock" suggested.
            ReceiveRoomRules.Room(80000, 70000, 90).ShouldBe(1999);
            ReceiveRoomRules.Room(80000, 72000, 90).ShouldBe(0);
        }
    }
}
