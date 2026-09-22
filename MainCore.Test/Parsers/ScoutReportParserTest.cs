using HtmlAgilityPack;
using MainCore.Commands.Features.RaidReport;
using MainCore.Parsers;

namespace MainCore.Test.Parsers
{
    // Fixtures: Parsers/ScoutReport/*.html - trimmed from REAL captures of /report/scouting and
    // one opened report (English client, 2026-09-21). Only the markup that is read was kept.
    public class ScoutReportParserTest
    {
        private static HtmlDocument Load(string name)
        {
            var doc = new HtmlDocument();
            doc.Load($"Parsers/ScoutReport/{name}.html");
            return doc;
        }

        [Fact]
        public void PageDetection_ListAndDetailAreToldApart()
        {
            var list = Load("ScoutReportList");
            var detail = Load("ScoutReportDetail_Success");

            ScoutReportParser.IsScoutReportListPage(list).ShouldBeTrue();
            ScoutReportParser.IsScoutReportDetailPage(list).ShouldBeFalse();

            ScoutReportParser.IsScoutReportDetailPage(detail).ShouldBeTrue();
            ScoutReportParser.IsScoutReportListPage(detail).ShouldBeFalse();
        }

        [Fact]
        public void GetReportRows_RealList_ReadsIdsAndTellsOurScoutingFromIncoming()
        {
            var rows = ScoutReportParser.GetReportRows(Load("ScoutReportList"));

            string.Join(",", rows.Select(x => x.ReportId)).ShouldBe("24872480,22534804,19129949,18785772,18775167,18772074");
            string.Join(",", rows.Select(x => x.Outcome)).ShouldBe("15,19,18,15,15,15");

            // 15 = our spying was successful; 18/19 = somebody else scouted us.
            string.Join(",", rows.Select(x => x.IsOurScouting)).ShouldBe("True,False,False,True,True,True");
        }

        [Fact]
        public void GetReportRows_DetailHref_StartsAtTheQuery()
        {
            var row = ScoutReportParser.GetReportRows(Load("ScoutReportList")).First();

            row.DetailHref.ShouldBe("?id=24872480%7C999be1f7&s=1");
        }

        [Fact]
        public void ParseDetail_RealReport_ReadsTilesResourcesAndCranny()
        {
            var detail = ScoutReportParser.ParseDetail(Load("ScoutReportDetail_Success"));

            detail.ShouldNotBeNull();
            detail!.AttackerTileId.ShouldBe(113427);
            detail.DefenderTileId.ShouldBe(113829);

            detail.Resources.ShouldNotBeNull();
            detail.Resources!.Lumber.ShouldBe(1638);
            detail.Resources.Clay.ShouldBe(1792);
            detail.Resources.Iron.ShouldBe(1266);
            detail.Resources.Crop.ShouldBe(1022);
            detail.Resources.Total.ShouldBe(5718);   // the report's own "carry" total

            detail.CrannyCapacity.ShouldBe(0);
        }

        [Fact]
        public void ParseDetail_TileIdsResolveToCoordinatesOnTheRadius200Map()
        {
            var detail = ScoutReportParser.ParseDetail(Load("ScoutReportDetail_Success"))!;

            // Our village (144|-82) fixes the radius, exactly like RaidReportRules does.
            MapTiles.InferRadius(detail.AttackerTileId, 144, -82).ShouldBe(200);

            var target = MapTiles.ToCoordinates(detail.DefenderTileId, 200);
            target.ShouldNotBeNull();
            target!.Value.X.ShouldBe(145);
            target.Value.Y.ShouldBe(-83);
        }

        [Fact]
        public void ParseDetail_ListPage_ReturnsNull()
        {
            ScoutReportParser.ParseDetail(Load("ScoutReportList")).ShouldBeNull();
        }

        [Theory]
        [InlineData(15, true)]
        [InlineData(16, true)]
        [InlineData(17, true)]
        [InlineData(18, false)]
        [InlineData(19, false)]
        [InlineData(0, false)]
        public void IsOurScoutingOutcome_MatchesThePagesOwnFilter(int outcome, bool expected)
        {
            ScoutReportParser.IsOurScoutingOutcome(outcome).ShouldBe(expected);
        }

        [Fact]
        public void EstimatePlunderable_NoCranny_IsTheWholeStock()
        {
            var resources = new ScoutResources(1638, 1792, 1266, 1022);

            ScoutReportRules.EstimatePlunderable(resources, 0).ShouldBe(5718);
            ScoutReportRules.EstimatePlunderable(resources, null).ShouldBe(5718);
        }

        [Fact]
        public void EstimatePlunderable_CrannyHidesUpToItsCapacityOfEachResource()
        {
            var resources = new ScoutResources(1000, 200, 500, 100);

            // 1000-300 + 0 + 500-300 + 0
            ScoutReportRules.EstimatePlunderable(resources, 300).ShouldBe(900);
            ScoutReportRules.EstimatePlunderable(resources, 5000).ShouldBe(0);
        }
    }
}
