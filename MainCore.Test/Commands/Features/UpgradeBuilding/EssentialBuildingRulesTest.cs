using MainCore.Commands.Features.UpgradeBuilding;
using MainCore.Enums;
using MainCore.Models;

namespace MainCore.Test.Commands.Features.UpgradeBuilding
{
    public class EssentialBuildingRulesTest
    {
        // A scanned village: slots 19..40 all present; `built` overrides the type/level of single slots.
        private static List<BuildingItem> Village(params (int Location, BuildingEnums Type, int Level)[] built)
        {
            var list = Enumerable.Range(19, 22)
                .Select(l => new BuildingItem { Location = l, Type = BuildingEnums.Site, CurrentLevel = 0 })
                .ToList();
            foreach (var (location, type, level) in built)
            {
                var item = list.First(x => x.Location == location);
                item.Type = type;
                item.CurrentLevel = level;
            }
            return list;
        }

        [Fact]
        public void EverythingPresent_NothingToDo()
        {
            var village = Village((26, BuildingEnums.MainBuilding, 5), (21, BuildingEnums.Granary, 1), (22, BuildingEnums.Warehouse, 1));
            EssentialBuildingRules.Plan(village).ShouldBeEmpty();
        }

        [Fact]
        public void AllThreeMissing_QueuesStorageFirstThenMainBuildingLast()
        {
            var plans = EssentialBuildingRules.Plan(Village());

            plans.Select(x => x.Type).ShouldBe([BuildingEnums.Granary, BuildingEnums.Warehouse, BuildingEnums.MainBuilding]);
            plans.ShouldAllBe(x => x.Level == EssentialBuildingRules.TargetLevel);
        }

        [Fact]
        public void MainBuildingGoesToSlot26()
        {
            var plans = EssentialBuildingRules.Plan(Village((21, BuildingEnums.Granary, 1), (22, BuildingEnums.Warehouse, 1)));

            plans.Count.ShouldBe(1);
            plans[0].Type.ShouldBe(BuildingEnums.MainBuilding);
            plans[0].Location.ShouldBe(26);
        }

        [Fact]
        public void StorageNeverTakesSlot26_39_Or_40_AndTwoPlansNeverShareASlot()
        {
            var plans = EssentialBuildingRules.Plan(Village());
            var storage = plans.Where(x => x.Type is BuildingEnums.Granary or BuildingEnums.Warehouse).ToList();

            storage.Select(x => x.Location).Distinct().Count().ShouldBe(2);
            storage.ShouldAllBe(x => x.Location != 26 && x.Location != 39 && x.Location != 40);
            storage.Select(x => x.Location).ShouldBe([19, 20]);
        }

        [Fact]
        public void OnlyGranaryMissing_OnlyGranaryIsQueued()
        {
            var plans = EssentialBuildingRules.Plan(Village((26, BuildingEnums.MainBuilding, 1), (19, BuildingEnums.Warehouse, 3)));

            plans.Count.ShouldBe(1);
            plans[0].Type.ShouldBe(BuildingEnums.Granary);
            plans[0].Location.ShouldBe(20);
        }

        [Fact]
        public void LayoutNotScannedYet_NothingIsMissing()
        {
            // a village whose buildings page was never read has (almost) no entries: unknown, not "destroyed"
            var village = new List<BuildingItem> { new() { Location = 26, Type = BuildingEnums.Site } };
            EssentialBuildingRules.Plan(village).ShouldBeEmpty();
            EssentialBuildingRules.IsLayoutScanned(village).ShouldBeFalse();
        }

        [Fact]
        public void MainBuildingSlotHoldsSomethingElse_NoMainBuildingPlan()
        {
            var village = Village((26, BuildingEnums.Barracks, 3), (21, BuildingEnums.Granary, 1), (22, BuildingEnums.Warehouse, 1));
            EssentialBuildingRules.Plan(village).ShouldBeEmpty();
        }

        [Fact]
        public void NoFreeSiteForStorage_SkipsIt()
        {
            var village = Village(Enumerable.Range(19, 22)
                .Where(l => l != 26)
                .Select(l => (l, BuildingEnums.Barracks, 1))
                .ToArray());

            var plans = EssentialBuildingRules.Plan(village);

            plans.Select(x => x.Type).ShouldBe([BuildingEnums.MainBuilding]);
        }
    }
}
