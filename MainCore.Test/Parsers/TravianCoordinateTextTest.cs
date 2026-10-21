using MainCore.Parsers;

namespace MainCore.Test.Parsers
{
    public class TravianCoordinateTextTest
    {
        // Exactly what Travian renders for "(35|-5)": LRO/PDF bidi characters and U+2212.
        private const string Raw = "Raid unoccupied oasis \u202d(\u202d35\u202c|\u202d\u2212\u202d5\u202c\u202c)\u202c";

        [Fact]
        public void Normalize_StripsBidiCharsWhitespaceAndUnifiesMinus()
        {
            TravianCoordinateText.Normalize(Raw).ShouldBe("Raidunoccupiedoasis(35|-5)");
        }

        [Fact]
        public void ContainsCoordinate_MatchesNegativeY()
        {
            TravianCoordinateText.ContainsCoordinate(Raw, 35, -5).ShouldBeTrue();
        }

        [Fact]
        public void ContainsCoordinate_DoesNotMatchOtherCoordinate()
        {
            TravianCoordinateText.ContainsCoordinate(Raw, 35, 5).ShouldBeFalse();
            TravianCoordinateText.ContainsCoordinate(Raw, 3, -5).ShouldBeFalse();
        }

        [Fact]
        public void ContainsCoordinate_PlainAsciiStillWorks()
        {
            TravianCoordinateText.ContainsCoordinate("Founding a new village (10|20)", 10, 20).ShouldBeTrue();
        }

        [Fact]
        public void ContainsCoordinate_HtmlEntityMinus()
        {
            TravianCoordinateText.ContainsCoordinate("Attack (4|&minus;7)", 4, -7).ShouldBeTrue();
        }

        [Fact]
        public void Normalize_NullOrEmpty_IsEmpty()
        {
            TravianCoordinateText.Normalize(null).ShouldBe(string.Empty);
            TravianCoordinateText.Normalize("").ShouldBe(string.Empty);
        }
    }
}
