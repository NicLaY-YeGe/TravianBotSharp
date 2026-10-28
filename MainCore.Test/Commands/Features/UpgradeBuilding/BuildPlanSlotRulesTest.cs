using MainCore.Commands.Features.UpgradeBuilding;

namespace MainCore.Test.Commands.Features.UpgradeBuilding
{
    public class BuildPlanSlotRulesTest
    {
        [Fact]
        public void DifferentBuildingInTheSlot_IsRejected()
        {
            // The 2026-10-28 incident: plan Brickyard, slot holds Marketplace level 13.
            BuildPlanSlotRules.IsOccupiedByOtherBuilding(BuildingEnums.Marketplace, 13, BuildingEnums.Brickyard, false).ShouldBeTrue();
        }

        [Fact]
        public void SameBuilding_IsFine()
        {
            BuildPlanSlotRules.IsOccupiedByOtherBuilding(BuildingEnums.Brickyard, 3, BuildingEnums.Brickyard, false).ShouldBeFalse();
        }

        [Fact]
        public void EmptySite_IsFine()
        {
            BuildPlanSlotRules.IsOccupiedByOtherBuilding(BuildingEnums.Site, 0, BuildingEnums.Brickyard, false).ShouldBeFalse();
            BuildPlanSlotRules.IsOccupiedByOtherBuilding(BuildingEnums.Marketplace, -1, BuildingEnums.Brickyard, false).ShouldBeFalse();
        }

        [Fact]
        public void UnknownBuilding_IsNotJudged()
        {
            BuildPlanSlotRules.IsOccupiedByOtherBuilding(BuildingEnums.Unknown, 5, BuildingEnums.Brickyard, false).ShouldBeFalse();
        }

        [Fact]
        public void PlanBuildingUnderConstructionHere_IsFine()
        {
            BuildPlanSlotRules.IsOccupiedByOtherBuilding(BuildingEnums.Marketplace, 0, BuildingEnums.Brickyard, true).ShouldBeFalse();
        }
    }
}
