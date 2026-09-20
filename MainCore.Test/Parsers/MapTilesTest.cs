using MainCore.Parsers;

namespace MainCore.Test.Parsers
{
    // The four points are REAL: tile ids from /karte.php?d=... links of captured reports next to
    // the coordinates the same reports print (2026-09-19, a server with map radius 200).
    public class MapTilesTest
    {
        [Theory]
        [InlineData(113427, 144, -82)]  // own village A_1201
        [InlineData(114225, 140, -84)]  // KingdomDuck Koyu (player village)
        [InlineData(110227, 152, -74)]  // unoccupied oasis, printed as (152|-74) in its report
        [InlineData(113032, 150, -81)]  // pejken city
        public void ToCoordinates_RealTileIds_MatchTheGameOnRadius200(int tileId, int x, int y)
        {
            MapTiles.ToCoordinates(tileId, 200).ShouldBe((x, y));
            MapTiles.ToTileId(x, y, 200).ShouldBe(tileId);
        }

        [Fact]
        public void InferRadius_OwnVillage_FindsTheServersRadius()
        {
            MapTiles.InferRadius(113427, 144, -82).ShouldBe(200);
        }

        [Fact]
        public void InferRadius_TileIdOfAnotherPlace_ReturnsNull()
        {
            // (144|-82) is not tile 114225 for any radius.
            MapTiles.InferRadius(114225, 144, -82).ShouldBeNull();
        }

        [Fact]
        public void ToCoordinates_OutsideTheMap_ReturnsNull()
        {
            MapTiles.ToCoordinates(0, 200).ShouldBeNull();
            MapTiles.ToCoordinates(401 * 401 + 1, 200).ShouldBeNull();
        }

        [Theory]
        [InlineData(400, 0, 0)]
        [InlineData(400, -400, 400)]
        [InlineData(400, 400, -400)]
        [InlineData(200, -37, 122)]
        public void ToTileId_And_ToCoordinates_RoundTrip(int radius, int x, int y)
        {
            var tileId = MapTiles.ToTileId(x, y, radius);

            MapTiles.ToCoordinates(tileId, radius).ShouldBe((x, y));
            MapTiles.InferRadius(tileId, x, y).ShouldBe(radius);
        }
    }
}
