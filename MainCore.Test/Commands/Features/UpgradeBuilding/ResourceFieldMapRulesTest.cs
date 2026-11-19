using MainCore.Commands.Features.UpgradeBuilding;

namespace MainCore.Test.Commands.Features.UpgradeBuilding
{
    public class ResourceFieldMapRulesTest
    {
        private const int Wood = 1, Clay = 2, Iron = 3, Crop = 4;

        // The list of the default template: wood 1, clay 5 and 6, iron 4 and 7, crop 8.
        private static readonly (int Type, int Location)[] TemplateUses =
        [
            (Wood, 1), (Clay, 6), (Iron, 7), (Crop, 8), (Clay, 5), (Iron, 4),
        ];

        private static List<(int Location, int Type)> Village(params (int Location, int Type)[] fields) => [.. fields];

        [Fact]
        public void LayoutTheListWasMadeFor_IsLeftUntouched()
        {
            var village = Village((1, Wood), (4, Iron), (5, Clay), (6, Clay), (7, Iron), (8, Crop), (2, Wood));
            var map = ResourceFieldMapRules.Build(TemplateUses, village);

            ResourceFieldMapRules.Resolve(map, Wood, 1).ShouldBe(1);
            ResourceFieldMapRules.Resolve(map, Clay, 5).ShouldBe(5);
            ResourceFieldMapRules.Resolve(map, Clay, 6).ShouldBe(6);
            ResourceFieldMapRules.Resolve(map, Iron, 4).ShouldBe(4);
            ResourceFieldMapRules.Resolve(map, Iron, 7).ShouldBe(7);
            ResourceFieldMapRules.Resolve(map, Crop, 8).ShouldBe(8);
        }

        [Fact]
        public void OtherLayout_PairsByTypeAndOrdinal()
        {
            // wood at 3, clay at 2 and 9, iron at 10 and 11, crop at 1
            var village = Village((3, Wood), (2, Clay), (9, Clay), (10, Iron), (11, Iron), (1, Crop));
            var map = ResourceFieldMapRules.Build(TemplateUses, village);

            ResourceFieldMapRules.Resolve(map, Wood, 1).ShouldBe(3);
            ResourceFieldMapRules.Resolve(map, Clay, 5).ShouldBe(2);   // lowest list location -> lowest village location
            ResourceFieldMapRules.Resolve(map, Clay, 6).ShouldBe(9);
            ResourceFieldMapRules.Resolve(map, Iron, 4).ShouldBe(10);
            ResourceFieldMapRules.Resolve(map, Iron, 7).ShouldBe(11);
            ResourceFieldMapRules.Resolve(map, Crop, 8).ShouldBe(1);
        }

        [Fact]
        public void TwoListLocationsNeverShareOneField()
        {
            var village = Village((3, Wood), (2, Clay), (9, Clay), (10, Iron), (11, Iron), (1, Crop));
            var map = ResourceFieldMapRules.Build(TemplateUses, village);

            var clay = new[] { ResourceFieldMapRules.Resolve(map, Clay, 5), ResourceFieldMapRules.Resolve(map, Clay, 6) };
            clay.Distinct().Count().ShouldBe(2);
        }

        [Fact]
        public void VillageWithFewerFieldsOfAType_DropsTheExtraListLocations()
        {
            // only one clay field: the second clay location of the list has no counterpart
            var village = Village((3, Wood), (9, Clay), (10, Iron), (11, Iron), (1, Crop));
            var map = ResourceFieldMapRules.Build(TemplateUses, village);

            ResourceFieldMapRules.Resolve(map, Clay, 5).ShouldBe(9);
            ResourceFieldMapRules.Resolve(map, Clay, 6).ShouldBe(ResourceFieldMapRules.NoField);
        }

        [Fact]
        public void VillageWithoutAnyFieldOfAType_DropsAllItsLocations()
        {
            var village = Village((3, Wood), (2, Clay), (10, Iron), (11, Iron));
            var map = ResourceFieldMapRules.Build(TemplateUses, village);

            ResourceFieldMapRules.Resolve(map, Crop, 8).ShouldBe(ResourceFieldMapRules.NoField);
        }

        [Fact]
        public void NonFieldTypes_AreIgnoredAndKeepTheirLocation()
        {
            var village = Village((3, Wood), (24, 10), (26, 15));
            var map = ResourceFieldMapRules.Build(TemplateUses, village);

            ResourceFieldMapRules.Resolve(map, 10, 24).ShouldBe(24);   // Warehouse is not a field
            ResourceFieldMapRules.Resolve(map, 15, 26).ShouldBe(26);
        }

        [Fact]
        public void LocationNotInTheList_IsNoField()
        {
            var village = Village((1, Wood));
            var map = ResourceFieldMapRules.Build(TemplateUses, village);

            ResourceFieldMapRules.Resolve(map, Wood, 17).ShouldBe(ResourceFieldMapRules.NoField);
        }
    }
}
