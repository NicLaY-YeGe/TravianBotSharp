using MainCore.Commands.Features.UpgradeBuilding;
using MainCore.Entities;
using MainCore.Enums;
using MainCore.Models;

namespace MainCore.Test.Commands.Features.UpgradeBuilding
{
    public class ResolveMissingPrerequisiteCommandTest
    {
        [Fact]
        public void GetPrerequisitePlan_ExistingBuildingBelowLevel_BumpsItByOneLevel()
        {
            // Arrange - village has a GrainMill at level 3, sitting at location 22.
            var buildings = new List<BuildingItem>()
            {
                new() { Id = new(1), Location = 22, Type = BuildingEnums.GrainMill, CurrentLevel = 3 },
                new() { Id = new(2), Location = 25, Type = BuildingEnums.Site },
            };

            // Act
            var plan = ResolveMissingPrerequisiteCommand.GetPrerequisitePlan(buildings, BuildingEnums.GrainMill);

            // Assert
            plan.ShouldNotBeNull();
            plan.Location.ShouldBe(22);
            plan.Type.ShouldBe(BuildingEnums.GrainMill);
            plan.Level.ShouldBe(4);
        }

        [Fact]
        public void GetPrerequisitePlan_MultipleExistingInstances_PicksHighestLevelOne()
        {
            // Arrange - Warehouse can have several instances (IsMultipleBuilding); the
            // resolver should bump the most-built one rather than a fresh/low one.
            var buildings = new List<BuildingItem>()
            {
                new() { Id = new(1), Location = 20, Type = BuildingEnums.Warehouse, CurrentLevel = 5 },
                new() { Id = new(2), Location = 21, Type = BuildingEnums.Warehouse, CurrentLevel = 1 },
            };

            // Act
            var plan = ResolveMissingPrerequisiteCommand.GetPrerequisitePlan(buildings, BuildingEnums.Warehouse);

            // Assert
            plan.ShouldNotBeNull();
            plan.Location.ShouldBe(20);
            plan.Level.ShouldBe(6);
        }

        [Fact]
        public void GetPrerequisitePlan_NoExistingBuilding_UsesFirstEmptyInfrastructurePlot()
        {
            // Arrange - no GrainMill anywhere yet; two empty (Site) plots available, an
            // out-of-range one that must be ignored (resource field slot), and two in the
            // 19-39 infrastructure range that should be picked in ascending location order.
            var buildings = new List<BuildingItem>()
            {
                new() { Id = new(1), Location = 5, Type = BuildingEnums.Site },
                new() { Id = new(2), Location = 30, Type = BuildingEnums.Site },
                new() { Id = new(3), Location = 21, Type = BuildingEnums.Site },
                new() { Id = new(4), Location = 19, Type = BuildingEnums.MainBuilding, CurrentLevel = 10 },
            };

            // Act
            var plan = ResolveMissingPrerequisiteCommand.GetPrerequisitePlan(buildings, BuildingEnums.GrainMill);

            // Assert
            plan.ShouldNotBeNull();
            plan.Location.ShouldBe(21);
            plan.Type.ShouldBe(BuildingEnums.GrainMill);
            plan.Level.ShouldBe(1);
        }

        [Fact]
        public void GetPrerequisitePlan_NoExistingBuildingAndNoEmptyPlot_ReturnsNull()
        {
            // Arrange - every infrastructure plot is already something else, no Site left.
            var buildings = new List<BuildingItem>()
            {
                new() { Id = new(1), Location = 19, Type = BuildingEnums.MainBuilding, CurrentLevel = 10 },
                new() { Id = new(2), Location = 20, Type = BuildingEnums.Warehouse, CurrentLevel = 5 },
                new() { Id = new(3), Location = 5, Type = BuildingEnums.Site }, // out of range, must be ignored
            };

            // Act
            var plan = ResolveMissingPrerequisiteCommand.GetPrerequisitePlan(buildings, BuildingEnums.GrainMill);

            // Assert
            plan.ShouldBeNull();
        }

        [Fact]
        public void GetPrerequisitePlan_PendingJobAlreadyClaimsASite_TreatsItAsExisting()
        {
            // Arrange - GetLayoutBuildingsCommand overlays a pending Job's target type onto a
            // Site location (see GetLayoutBuildingsCommand.cs). Once that overlay has happened,
            // the resolver should treat it exactly like a real (if still level-0) building of
            // that type, so a second prerequisite-search for the same type in a later loop
            // iteration bumps this one instead of grabbing a different empty plot.
            var buildings = new List<BuildingItem>()
            {
                new() { Id = new(1), Location = 22, Type = BuildingEnums.GrainMill, CurrentLevel = 0, JobLevel = 1 },
                new() { Id = new(2), Location = 25, Type = BuildingEnums.Site },
            };

            // Act
            var plan = ResolveMissingPrerequisiteCommand.GetPrerequisitePlan(buildings, BuildingEnums.GrainMill);

            // Assert
            plan.ShouldNotBeNull();
            plan.Location.ShouldBe(22);
            plan.Level.ShouldBe(2);
        }
    }
}
