using HtmlAgilityPack;
using MainCore.Parsers;

namespace MainCore.Test.Parsers
{
    // Fixtures: Parsers/OffensiveReport/*.html - trimmed from REAL captures of /report/offensive
    // and three opened reports (English client, 2026-09-19) - except OffensiveReportList_Synthetic,
    // whose two rows are the real markup with only the result class changed (no real loss row was
    // available yet).
    public class OffensiveReportParserTest
    {
        private static HtmlDocument Load(string name)
        {
            var doc = new HtmlDocument();
            doc.Load($"Parsers/OffensiveReport/{name}.html");
            return doc;
        }

        [Fact]
        public void PageDetection_ListAndDetailAreToldApart()
        {
            var list = Load("OffensiveReportList");
            var detail = Load("OffensiveReportDetail_PlayerVillage");

            // Both pages carry #content.reportsOffensive - the content class alone is not enough.
            OffensiveReportParser.IsOffensiveReportListPage(list).ShouldBeTrue();
            OffensiveReportParser.IsOffensiveReportDetailPage(list).ShouldBeFalse();

            OffensiveReportParser.IsOffensiveReportDetailPage(detail).ShouldBeTrue();
            OffensiveReportParser.IsOffensiveReportListPage(detail).ShouldBeFalse();
        }

        [Fact]
        public void HasNextPage_ListWithPaginatorNextLink_ReturnsTrue()
        {
            OffensiveReportParser.HasNextPage(Load("OffensiveReportList")).ShouldBeTrue();
            OffensiveReportParser.HasNextPage(Load("OffensiveReportDetail_PlayerVillage")).ShouldBeFalse();
        }

        [Fact]
        public void GetReportRows_RealList_ReadsIdsOutcomesAndLoot()
        {
            var rows = OffensiveReportParser.GetReportRows(Load("OffensiveReportList"));

            string.Join(",", rows.Select(x => x.ReportId)).ShouldBe("19345928,19288771,18963012,18797988,18581218");
            rows.ShouldAllBe(x => x.Outcome == 1);

            var kingdomDuck = rows.Single(x => x.ReportId == 18963012);
            kingdomDuck.HasLootInfo.ShouldBeTrue();
            kingdomDuck.LootCarried.ShouldBe(42);
            kingdomDuck.LootCapacity.ShouldBe(225);

            // "half" is only a coarse class - 169/180 is 94% and must stay readable as such.
            var nearlyFull = rows.Single(x => x.ReportId == 18797988);
            nearlyFull.LootCarried.ShouldBe(169);
            nearlyFull.LootCapacity.ShouldBe(180);
        }

        [Fact]
        public void GetReportRows_HeroOnlyOasisRaid_HasNoLootInfo()
        {
            // The two icon-less rows of the real list: nothing could be carried, so the game does
            // not render a carry icon at all.
            var row = OffensiveReportParser.GetReportRows(Load("OffensiveReportList")).Single(x => x.ReportId == 19288771);

            row.HasLootInfo.ShouldBeFalse();
        }

        [Fact]
        public void GetReportRows_OasisSubject_ReadsCoordinatesDespiteMinusSignAndBidiMarks()
        {
            // "(&#x202d;157&#x202c;|&#x202d;&minus;&#x202d;81..." - U+2212 is not a hyphen.
            var row = OffensiveReportParser.GetReportRows(Load("OffensiveReportList")).Single(x => x.ReportId == 19345928);

            row.HasCoordinates.ShouldBeTrue();
            row.TargetX.ShouldBe(157);
            row.TargetY.ShouldBe(-81);
        }

        [Fact]
        public void GetReportRows_LiveDom_ReadsTheSameThingWithoutTitleAttributesAndWithLiteralMarks()
        {
            var row = OffensiveReportParser.GetReportRows(Load("OffensiveReportList_LiveDom")).Single();

            row.ReportId.ShouldBe(19345928);
            row.LootCarried.ShouldBe(325);
            row.LootCapacity.ShouldBe(325);
            row.HasCoordinates.ShouldBeTrue();
            row.TargetX.ShouldBe(157);
            row.TargetY.ShouldBe(-81);
            row.DetailHref.ShouldBe("?id=19345928%7C75e76c3b&s=1");
        }

        [Fact]
        public void GetReportRows_PlayerVillageSubject_HasNoCoordinates()
        {
            var rows = OffensiveReportParser.GetReportRows(Load("OffensiveReportList"));

            rows.Single(x => x.ReportId == 18963012).HasCoordinates.ShouldBeFalse();
            rows.Single(x => x.ReportId == 18797988).HasCoordinates.ShouldBeFalse();
        }

        [Fact]
        public void GetReportRows_CoordinatesInTheVillageName_AreUsed()
        {
            // "A_1201 raids Natars 141|-76" - the only place these coordinates appear.
            var row = OffensiveReportParser.GetReportRows(Load("OffensiveReportList")).Single(x => x.ReportId == 18581218);

            row.HasCoordinates.ShouldBeTrue();
            row.TargetX.ShouldBe(141);
            row.TargetY.ShouldBe(-76);
        }

        [Fact]
        public void GetReportRows_DetailHref_IsDecodedAndRelative()
        {
            var row = OffensiveReportParser.GetReportRows(Load("OffensiveReportList")).First();

            row.DetailHref.ShouldBe("?id=19345928%7C75e76c3b&s=1");
        }

        [Fact]
        public void GetReportRows_SyntheticLossRows_ReadOutcomeTwoAndThree()
        {
            var rows = OffensiveReportParser.GetReportRows(Load("OffensiveReportList_Synthetic"));

            var lost = rows.Single(x => x.ReportId == 19518262);
            lost.Outcome.ShouldBe(3);
            lost.HasLootInfo.ShouldBeFalse();

            var lossy = rows.Single(x => x.ReportId == 19500000);
            lossy.Outcome.ShouldBe(2);
            lossy.LootCarried.ShouldBe(1234);
            lossy.LootCapacity.ShouldBe(2000);
        }

        [Fact]
        public void GetReportRows_DetailPage_ReturnsNothing()
        {
            OffensiveReportParser.GetReportRows(Load("OffensiveReportDetail_PlayerVillage")).ShouldBeEmpty();
        }

        [Fact]
        public void ParseDetail_PlayerVillageRaid_ReadsTilesTroopsAndBounty()
        {
            var detail = OffensiveReportParser.ParseDetail(Load("OffensiveReportDetail_PlayerVillage"));

            detail.ShouldNotBeNull();
            detail.AttackerTileId.ShouldBe(113427);
            detail.DefenderTileId.ShouldBe(114225);
            detail.HasDefenderCoordinates.ShouldBeFalse();
            detail.TroopsSent.ShouldBe(5);
            detail.TroopsDead.ShouldBe(0);
            detail.LootCarried.ShouldBe(42);
            detail.LootCapacity.ShouldBe(225);
        }

        [Fact]
        public void ParseDetail_HeroOnlyOasisRaid_ReadsCoordinatesAndEmptyBounty()
        {
            var detail = OffensiveReportParser.ParseDetail(Load("OffensiveReportDetail_HeroOasis"));

            detail.ShouldNotBeNull();
            detail.HasDefenderCoordinates.ShouldBeTrue();
            detail.DefenderX.ShouldBe(152);
            detail.DefenderY.ShouldBe(-74);
            detail.DefenderTileId.ShouldBe(110227);
            detail.TroopsSent.ShouldBe(1);
            detail.TroopsDead.ShouldBe(0);
            detail.LootCarried.ShouldBe(0);
            detail.LootCapacity.ShouldBe(0);
        }

        [Fact]
        public void ParseDetail_LostRaid_CountsAllDeadAndToleratesHiddenDefenderTroops()
        {
            // The defender's troops are "?" - must not throw or leak into the attacker's totals.
            var detail = OffensiveReportParser.ParseDetail(Load("OffensiveReportDetail_Lost"));

            detail.ShouldNotBeNull();
            detail.TroopsSent.ShouldBe(1);
            detail.TroopsDead.ShouldBe(1);
            detail.DefenderTileId.ShouldBe(113032);
        }

        [Fact]
        public void ParseDetail_ListPage_ReturnsNull()
        {
            OffensiveReportParser.ParseDetail(Load("OffensiveReportList")).ShouldBeNull();
        }

        [Theory]
        [InlineData("42/225", 42, 225)]
        [InlineData("\u202d\u202d42\u202c/\u202d225\u202c\u202c", 42, 225)]
        [InlineData("1,234/2,000", 1234, 2000)]
        [InlineData("0/0", 0, 0)]
        public void TryParseFraction_ReadsCarryTexts(string text, int carried, int capacity)
        {
            OffensiveReportParser.TryParseFraction(text, out var actualCarried, out var actualCapacity).ShouldBeTrue();
            actualCarried.ShouldBe(carried);
            actualCapacity.ShouldBe(capacity);
        }

        [Theory]
        [InlineData("")]
        [InlineData("no numbers here")]
        [InlineData("42")]
        public void TryParseFraction_NotAFraction_ReturnsFalse(string text)
        {
            OffensiveReportParser.TryParseFraction(text, out _, out _).ShouldBeFalse();
        }

        [Fact]
        public void NormalizeText_ReplacesRealMinusSignAndDropsBidiMarks()
        {
            OffensiveReportParser.NormalizeText("\u202d\u2212\u202d81\u202c\u202c)").ShouldBe("-81)");
            OffensiveReportParser.NormalizeText("&#x202d;&minus;&#x202d;81&#x202c;").ShouldBe("-81");
        }
    }
}
