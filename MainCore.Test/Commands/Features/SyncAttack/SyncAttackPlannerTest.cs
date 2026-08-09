using MainCore.Commands.Features.SyncAttack;
using MainCore.Enums;

namespace MainCore.Test.Commands.Features.SyncAttack
{
    public class SyncAttackPlannerTest
    {
        [Fact]
        public void ComputeSendTimes_Earliest_UsesSlowestVillagePlusBuffer()
        {
            // Arrange
            var now = new DateTime(2026, 1, 1, 12, 0, 0);
            var fast = new VillageId(1);
            var slow = new VillageId(2);
            var travelTimes = new Dictionary<VillageId, TimeSpan>
            {
                [fast] = TimeSpan.FromMinutes(10),
                [slow] = TimeSpan.FromMinutes(30),
            };

            // Act
            var result = MainCore.Commands.Features.SyncAttack.SyncAttackPlanner.ComputeSendTimes(
                travelTimes, SyncAttackArrivalModeEnums.Earliest, null, now);

            // Assert
            result.IsSuccess.ShouldBeTrue();
            var plan = result.Value;
            plan.ArrivalTime.ShouldBe(now.Add(TimeSpan.FromMinutes(30)).Add(MainCore.Commands.Features.SyncAttack.SyncAttackPlanner.SafetyBuffer));
            plan.SendTimes[fast].ShouldBe(plan.ArrivalTime - TimeSpan.FromMinutes(10));
            plan.SendTimes[slow].ShouldBe(plan.ArrivalTime - TimeSpan.FromMinutes(30));
        }

        [Fact]
        public void ComputeSendTimes_AllVillagesArriveAtTheExactSameInstant()
        {
            // Arrange
            var now = new DateTime(2026, 1, 1, 12, 0, 0);
            var travelTimes = new Dictionary<VillageId, TimeSpan>
            {
                [new VillageId(1)] = TimeSpan.FromMinutes(5),
                [new VillageId(2)] = TimeSpan.FromHours(2),
                [new VillageId(3)] = TimeSpan.FromMinutes(47),
            };

            // Act
            var result = SyncAttackPlanner.ComputeSendTimes(travelTimes, SyncAttackArrivalModeEnums.Earliest, null, now);

            // Assert
            result.IsSuccess.ShouldBeTrue();
            var plan = result.Value;
            foreach (var (villageId, sendTime) in plan.SendTimes)
            {
                (sendTime + travelTimes[villageId]).ShouldBe(plan.ArrivalTime);
            }
        }

        [Fact]
        public void ComputeSendTimes_SpecificArrival_ReachableBySlowestVillage_Succeeds()
        {
            // Arrange
            var now = new DateTime(2026, 1, 1, 12, 0, 0);
            var travelTimes = new Dictionary<VillageId, TimeSpan>
            {
                [new VillageId(1)] = TimeSpan.FromMinutes(10),
            };
            var desiredArrival = now.AddHours(1);

            // Act
            var result = SyncAttackPlanner.ComputeSendTimes(travelTimes, SyncAttackArrivalModeEnums.Specific, desiredArrival, now);

            // Assert
            result.IsSuccess.ShouldBeTrue();
            result.Value.ArrivalTime.ShouldBe(desiredArrival);
            result.Value.SendTimes[new VillageId(1)].ShouldBe(desiredArrival - TimeSpan.FromMinutes(10));
        }

        [Fact]
        public void ComputeSendTimes_SpecificArrival_TooSoonForSlowestVillage_Fails()
        {
            // Arrange
            var now = new DateTime(2026, 1, 1, 12, 0, 0);
            var travelTimes = new Dictionary<VillageId, TimeSpan>
            {
                [new VillageId(1)] = TimeSpan.FromHours(5),
            };
            var desiredArrival = now.AddMinutes(30); // way less than the 5h travel time

            // Act
            var result = SyncAttackPlanner.ComputeSendTimes(travelTimes, SyncAttackArrivalModeEnums.Specific, desiredArrival, now);

            // Assert
            result.IsFailed.ShouldBeTrue();
        }

        [Fact]
        public void ComputeSendTimes_SpecificArrival_MissingDesiredTime_Fails()
        {
            // Arrange
            var now = new DateTime(2026, 1, 1, 12, 0, 0);
            var travelTimes = new Dictionary<VillageId, TimeSpan>
            {
                [new VillageId(1)] = TimeSpan.FromMinutes(10),
            };

            // Act
            var result = SyncAttackPlanner.ComputeSendTimes(travelTimes, SyncAttackArrivalModeEnums.Specific, null, now);

            // Assert
            result.IsFailed.ShouldBeTrue();
        }

        [Fact]
        public void ComputeSendTimes_NoTravelTimes_Fails()
        {
            // Arrange
            var now = new DateTime(2026, 1, 1, 12, 0, 0);
            var travelTimes = new Dictionary<VillageId, TimeSpan>();

            // Act
            var result = SyncAttackPlanner.ComputeSendTimes(travelTimes, SyncAttackArrivalModeEnums.Earliest, null, now);

            // Assert
            result.IsFailed.ShouldBeTrue();
        }
    }
}
