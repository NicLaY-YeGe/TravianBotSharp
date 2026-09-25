using MainCore.Commands.Features.CropScan;

namespace MainCore.Test.Commands.Features.CropScan
{
    public class CropScanCoordinateRulesTest
    {
        [Fact]
        public void GetCoordinatesByDistance_ExcludesCenterItself()
        {
            var coordinates = CropScanCoordinateRules.GetCoordinatesByDistance(33, -2, 5);
            coordinates.Any(c => c.X == 33 && c.Y == -2).ShouldBeFalse();
        }

        [Fact]
        public void GetCoordinatesByDistance_SortedNearestFirst()
        {
            var coordinates = CropScanCoordinateRules.GetCoordinatesByDistance(0, 0, 3);

            for (var i = 1; i < coordinates.Count; i++)
            {
                (coordinates[i].Distance >= coordinates[i - 1].Distance).ShouldBeTrue();
            }

            // The 4 orthogonal neighbours are the closest possible (distance 1).
            coordinates[0].Distance.ShouldBe(1.0);
        }

        [Fact]
        public void GetCoordinatesByDistance_MatchesRealCaptureDistance()
        {
            // Real capture (2026-09-24): center (33|-2), tile (18|-1) -> "15.03 fields".
            var coordinates = CropScanCoordinateRules.GetCoordinatesByDistance(33, -2, 16);
            var match = coordinates.First(c => c.X == 18 && c.Y == -1);

            Math.Round(match.Distance, 2).ShouldBe(15.03);
        }

        [Fact]
        public void GetCoordinatesByDistance_NothingBeyondRadius()
        {
            var maxDistance = 10;
            var coordinates = CropScanCoordinateRules.GetCoordinatesByDistance(0, 0, maxDistance);

            coordinates.All(c => c.Distance <= maxDistance).ShouldBeTrue();
        }

        [Fact]
        public void GetCoordinatesByDistance_ZeroOrNegativeDistance_ReturnsEmpty()
        {
            CropScanCoordinateRules.GetCoordinatesByDistance(0, 0, 0).Count.ShouldBe(0);
            CropScanCoordinateRules.GetCoordinatesByDistance(0, 0, -5).Count.ShouldBe(0);
        }
    }
}
