using HtmlAgilityPack;
using MainCore.Enums;
using MainCore.Parsers;

namespace MainCore.Test.Parsers
{
    // Reuses the same real-capture fixtures as OffensiveReportParserTest/ScoutReportParserTest -
    // see their class comments for provenance. This test drives ReportTroopTableParser directly
    // against each fixture's defender role block, bypassing ParseDetail's private wrapper lookup.
    public class ReportTroopTableParserTest
    {
        private static HtmlNode DefenderBlock(string path)
        {
            var doc = new HtmlDocument();
            doc.Load(path);

            var wrapper = doc.GetElementbyId("reportWrapper");
            return wrapper.Descendants("div").First(x => x.HasClass("role") && x.HasClass("defender"));
        }

        [Fact]
        public void GetColumns_OffensiveReportDefender_ReadsGlobalTroopIdsFromHeaderIcons()
        {
            var defender = DefenderBlock("Parsers/OffensiveReport/OffensiveReportDetail_Lost.html");

            var columns = ReportTroopTableParser.GetColumns(defender);

            string.Join(",", columns).ShouldBe("Clubswinger,Spearman");
        }

        [Fact]
        public void GetRow_HiddenDefenderTroopCount_EveryCellIsNull()
        {
            var defender = DefenderBlock("Parsers/OffensiveReport/OffensiveReportDetail_Lost.html");

            var values = ReportTroopTableParser.GetRow(defender, "troopCount_small");

            values.Count.ShouldBe(2);
            values.ShouldAllBe(x => x == null);
        }

        [Fact]
        public void GetComposition_HiddenDefenderRow_ReturnsNull()
        {
            var defender = DefenderBlock("Parsers/OffensiveReport/OffensiveReportDetail_Lost.html");

            ReportTroopTableParser.GetComposition(defender, "troopCount_small").ShouldBeNull();
        }

        [Fact]
        public void GetComposition_RevealedAllZeroDefenderRow_ReturnsEmptyNotNull()
        {
            var defender = DefenderBlock("Parsers/ScoutReport/ScoutReportDetail_Success.html");

            var composition = ReportTroopTableParser.GetComposition(defender, "troopCount_small");

            composition.ShouldNotBeNull();
            composition!.Count.ShouldBe(0);
        }
    }
}
