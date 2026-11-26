using HtmlAgilityPack;
using MainCore.Parsers;

namespace MainCore.Test.Parsers
{
    // Fixture: Parsers/SurroundingReport/Surrounding_NewScheme.html - real capture of /report/surrounding on the
    // newer server scheme (2026-11-25, village 22459): coordinate links are /map?x=..&y=.., 10 rows:
    // 8 "An oasis has been plundered." (7 different tiles, (-31|28) twice, (-29|26) is 16.12 fields away) and
    // 2 "A fight took place" rows. Before 2026-11-25 the parser looked for "karte.php" and returned nothing.
    public class SurroundingReportParserTest
    {
        private const string Path = "Parsers/SurroundingReport/Surrounding_NewScheme.html";

        private static HtmlDocument Load()
        {
            var doc = new HtmlDocument();
            doc.Load(Path);
            return doc;
        }

        [Fact]
        public void IsSurroundingReportPage_True()
        {
            SurroundingReportParser.IsSurroundingReportPage(Load()).ShouldBeTrue();
        }

        [Fact]
        public void GetRows_ReadsAllTenRows()
        {
            SurroundingReportParser.GetRows(Load()).Count.ShouldBe(10);
        }

        [Fact]
        public void GetOasisReportRows_SkipsFightRows_AndReadsMapLinks()
        {
            var rows = SurroundingReportParser.GetOasisReportRows(Load());
            rows.Count.ShouldBe(8);
            rows.Any(r => r.X == -31 && r.Y == 28 && r.Distance == 13.42).ShouldBeTrue();
            rows.Any(r => r.X == -39 && r.Y == 36 && r.Distance == 4.47).ShouldBeTrue();
            rows.Any(r => r.X == -52 && r.Y == 34).ShouldBeFalse();
            rows.Any(r => r.X == -42 && r.Y == 27).ShouldBeFalse();
        }

        [Fact]
        public void GetUniqueOasisCoordinates_DedupesAndSortsNearestFirst()
        {
            var rows = SurroundingReportParser.GetUniqueOasisCoordinates(Load(), 15);
            string.Join(";", rows.Select(r => $"{r.X}|{r.Y}")).ShouldBe("-39|36;-42|39;-35|35;-32|28;-31|28;-32|25");
        }

        [Fact]
        public void GetUniqueOasisCoordinates_RespectsTheDistanceLimit()
        {
            SurroundingReportParser.GetUniqueOasisCoordinates(Load(), 10).Count.ShouldBe(3);
        }

        [Fact]
        public void OldKarteLinks_StillWork()
        {
            var doc = new HtmlDocument();
            doc.LoadHtml("<table id=\"overview\"><tbody><tr><td class=\"sub\"><div class=\"reportIcon oasisOccupy\"></div></td>" +
                         "<td class=\"coords\"><a href=\"/karte.php?x=3&amp;y=-4\">(3|-4)</a></td><td class=\"dist\">2,5</td></tr></tbody></table>");
            var rows = SurroundingReportParser.GetOasisReportRows(doc);
            string.Join(";", rows.Select(r => $"{r.X}|{r.Y}|{r.Distance}")).ShouldBe("3|-4|2.5");
        }
    }
}
