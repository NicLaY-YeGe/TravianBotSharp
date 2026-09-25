using HtmlAgilityPack;
using MainCore.Parsers;

namespace MainCore.Test.Parsers
{
    // Fixture: Parsers/CropScan/TileDetailsAbandonedValley.html - real capture, 2026-09-24,
    // (18|-1), 9 Croplands, 15.03 fields (see CLAUDE.md crop-scan feature notes).
    public class CropOasisTileParserTest
    {
        private static HtmlDocument Load(string name)
        {
            var doc = new HtmlDocument();
            doc.Load($"Parsers/CropScan/{name}.html");
            return doc;
        }

        [Fact]
        public void IsAbandonedValley_RealCapture_ReturnsTrue()
        {
            var doc = Load("TileDetailsAbandonedValley");
            CropOasisTileParser.IsAbandonedValley(doc).ShouldBeTrue();
        }

        [Fact]
        public void IsAbandonedValley_NotAnOasisDialog_ReturnsFalse()
        {
            var doc = new HtmlDocument();
            doc.LoadHtml("<div>no tileDetails here</div>");
            CropOasisTileParser.IsAbandonedValley(doc).ShouldBeFalse();
        }

        [Fact]
        public void GetCoordinates_RealCapture_ReadsBidiWrappedMinusCorrectly()
        {
            var doc = Load("TileDetailsAbandonedValley");
            var coordinates = CropOasisTileParser.GetCoordinates(doc);

            coordinates.ShouldNotBeNull();
            coordinates!.Value.X.ShouldBe(18);
            coordinates.Value.Y.ShouldBe(-1);
        }

        [Fact]
        public void GetCroplands_RealCapture_ReadsR4RowDirectly()
        {
            var doc = Load("TileDetailsAbandonedValley");
            CropOasisTileParser.GetCroplands(doc).ShouldBe(9);
        }

        [Fact]
        public void GetDistance_RealCapture_ParsesDecimalFromFieldsText()
        {
            var doc = Load("TileDetailsAbandonedValley");
            var distance = CropOasisTileParser.GetDistance(doc);

            distance.ShouldNotBeNull();
            Math.Round(distance!.Value, 2).ShouldBe(15.03);
        }
    }
}
