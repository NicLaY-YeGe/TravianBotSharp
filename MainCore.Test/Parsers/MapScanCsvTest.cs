using MainCore.Parsers;
using System.Globalization;

namespace MainCore.Test.Parsers
{
    public class MapScanCsvTest
    {
        private static MapVillage Village(int x, int y, string name = "Village", string player = "Player", string tag = "ALY", int pop = 123, bool capital = false, int tid = 1)
            => new(1, x, y, tid, 1, name, 1, player, 1, tag, pop, capital);

        [Theory]
        [InlineData(-12, 8, "-12|8")]
        [InlineData(0, 0, "0|0")]
        [InlineData(35, -5, "35|-5")]
        public void Coordinate_IsOneColumnLikeTheGame(int x, int y, string expected)
        {
            MapScanCsv.Coordinate(x, y).ShouldBe(expected);
        }

        [Fact]
        public void Build_WritesHeaderAndOneRowPerVillage_CoordinateInOneColumn()
        {
            var rows = new[] { (2.5, Village(-12, 8, "Alpha", "Bob", "TAG", 456, true)) };

            var lines = MapScanCsv.Build(rows, CultureInfo.InvariantCulture).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

            lines.Length.ShouldBe(2);
            lines[0].ShouldBe("Mesafe;Koordinat;Köy;Oyuncu;Kabile;İttifak;Nüfus;Başkent");
            lines[1].ShouldBe("2.50;-12|8;Alpha;Bob;Romans;TAG;456;Evet");
        }

        [Fact]
        public void Build_UsesDecimalCommaForTurkishCulture()
        {
            var rows = new[] { (12.3456, Village(1, 2)) };

            var line = MapScanCsv.Build(rows, new CultureInfo("tr-TR")).Split("\r\n", StringSplitOptions.RemoveEmptyEntries)[1];

            line.ShouldStartWith("12,35;1|2;");
        }

        [Fact]
        public void Build_KeepsTheGivenOrder()
        {
            var rows = new[] { (1.0, Village(1, 1, "A")), (2.0, Village(2, 2, "B")), (3.0, Village(3, 3, "C")) };

            var lines = MapScanCsv.Build(rows, CultureInfo.InvariantCulture).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

            lines[1].ShouldContain(";A;");
            lines[2].ShouldContain(";B;");
            lines[3].ShouldContain(";C;");
        }

        [Fact]
        public void Build_NoAlliance_IsEmptyField()
        {
            var rows = new[] { (1.0, Village(1, 1, tag: "")) };

            var line = MapScanCsv.Build(rows, CultureInfo.InvariantCulture).Split("\r\n", StringSplitOptions.RemoveEmptyEntries)[1];

            line.ShouldContain(";Romans;;123;Hayır");
        }

        [Theory]
        [InlineData("Plain", "Plain")]
        [InlineData("", "")]
        [InlineData("a;b", "\"a;b\"")]
        [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
        [InlineData("=SUM(A1)", "'=SUM(A1)")]
        [InlineData("+1", "'+1")]
        [InlineData("-x", "'-x")]
        [InlineData("@home", "'@home")]
        [InlineData("=a;b", "\"'=a;b\"")]
        public void Text_QuotesSeparatorsAndNeutralisesFormulas(string value, string expected)
        {
            MapScanCsv.Text(value).ShouldBe(expected);
        }

        [Fact]
        public void Text_Null_IsEmpty()
        {
            MapScanCsv.Text(null).ShouldBe("");
        }

        [Fact]
        public void TribeText_UnknownId_IsReadable()
        {
            MapScanCsv.TribeText(99).ShouldBe("Tribe 99");
        }
    }
}
