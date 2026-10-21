namespace MainCore.Test.Parsers
{
    public class RallyPointOverviewParser : BaseParser
    {
        private const string IncomingAttackPage = "Parsers/RallyPointOverview/IncomingAttack.html";

        // Real capture (2026-10-18): the timer seconds live in `value`, not `data-value`.
        [Fact]
        public void GetIncomingAttackSeconds_ReadsValueAttribute()
        {
            _html.Load(IncomingAttackPage);
            var actual = MainCore.Parsers.RallyPointOverviewParser.GetIncomingAttackSeconds(_html);
            actual.ShouldBe(424);
        }

        private const string OutgoingMovementsPage = "Parsers/RallyPointOverview/OutgoingMovements.html";

        // 2026-10-20 live capture: the headline coordinates are wrapped in invisible bidi characters
        // and use U+2212 as the minus sign, so a plain "(35|-5)" match never found the abort button.
        [Fact]
        public void GetOutgoingAttackAbortButton_MatchesCoordinatesWithBidiCharsAndUnicodeMinus()
        {
            _html.Load(OutgoingMovementsPage);

            var button = MainCore.Parsers.RallyPointOverviewParser.GetOutgoingAttackAbortButton(_html, 35, -5);

            button.ShouldNotBeNull();
            button!.GetAttributeValue("onclick", "").Contains("recall=112616942").ShouldBeTrue();
        }

        [Fact]
        public void GetOutgoingAttackAbortButton_PicksTheRowOfTheRequestedTarget()
        {
            _html.Load(OutgoingMovementsPage);

            var button = MainCore.Parsers.RallyPointOverviewParser.GetOutgoingAttackAbortButton(_html, 29, -1);

            button.ShouldNotBeNull();
            button!.GetAttributeValue("onclick", "").Contains("recall=112616943").ShouldBeTrue();
        }

        [Fact]
        public void GetOutgoingAttackAbortButton_RowWithoutAbortDiv_ReturnsNull()
        {
            _html.Load(OutgoingMovementsPage);

            MainCore.Parsers.RallyPointOverviewParser.GetOutgoingAttackAbortButton(_html, 27, 0).ShouldBeNull();
        }

        [Fact]
        public void GetOutgoingAttackAbortButton_UnknownTarget_ReturnsNull()
        {
            _html.Load(OutgoingMovementsPage);

            MainCore.Parsers.RallyPointOverviewParser.GetOutgoingAttackAbortButton(_html, 1, 1).ShouldBeNull();
        }

        [Fact]
        public void GetOutgoingMovementHeadlines_AreReadableForTheLog()
        {
            _html.Load(OutgoingMovementsPage);

            var headlines = MainCore.Parsers.RallyPointOverviewParser.GetOutgoingMovementHeadlines(_html);

            headlines.Count.ShouldBe(3);
            headlines[0].ShouldBe("Raidunoccupiedoasis(35|-5)");
        }
    }
}
