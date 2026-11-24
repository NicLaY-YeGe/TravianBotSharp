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

        private const string RaidAndAttackPage = "Parsers/RallyPointOverview/IncomingRaidAndAttack.html";

        // Real capture 2026-10-23: one raid ("X raids Y", table class inRaid, in 569 s) and one attack
        // ("X attacks Y", table class inAttack, in 2364 s). The raid was missed by the old inAttack-only
        // parser, so a village that was only raided looked flagged but had "no attack" on the page.
        [Fact]
        public void GetIncomingMovements_ReadsRaidAndAttack()
        {
            _html.Load(RaidAndAttackPage);

            var movements = MainCore.Parsers.RallyPointOverviewParser.GetIncomingMovements(_html);

            movements.Count.ShouldBe(2);
            var raid = movements.Single(x => x.Kind == MainCore.Parsers.RallyPointOverviewParser.IncomingKind.Raid);
            raid.Seconds.ShouldBe(569);
            raid.Headline.ShouldContain("raids");
            var attack = movements.Single(x => x.Kind == MainCore.Parsers.RallyPointOverviewParser.IncomingKind.Attack);
            attack.Seconds.ShouldBe(2364);
            attack.Headline.ShouldContain("attacks");
        }

        [Fact]
        public void GetIncomingAttackSeconds_IgnoresRaids()
        {
            _html.Load(RaidAndAttackPage);

            MainCore.Parsers.RallyPointOverviewParser.GetIncomingAttackSeconds(_html).ShouldBe(2364);
        }

        [Fact]
        public void GetIncomingHostileSeconds_RaidsIncluded_PicksTheSoonest()
        {
            _html.Load(RaidAndAttackPage);

            MainCore.Parsers.RallyPointOverviewParser.GetIncomingHostileSeconds(_html, includeRaids: true).ShouldBe(569);
            MainCore.Parsers.RallyPointOverviewParser.GetIncomingHostileSeconds(_html, includeRaids: false).ShouldBe(2364);
        }

        [Fact]
        public void OnlyARaid_HasNoIncomingAttack_ButIsStillReadAsAMovement()
        {
            _html.LoadHtml(
                "<table class=\"troop_details inRaid\"><thead><tr><td class=\"troopHeadline\">Bob raids Home</td></tr></thead>" +
                "<tbody class=\"infos\"><tr><td><span class=\"timer\" counting=\"down\" value=\"120\" data-value=\"120\">0:02:00</span></td></tr></tbody></table>");

            MainCore.Parsers.RallyPointOverviewParser.GetIncomingAttackSeconds(_html).ShouldBeNull();
            MainCore.Parsers.RallyPointOverviewParser.GetIncomingHostileSeconds(_html, includeRaids: true).ShouldBe(120);
            MainCore.Parsers.RallyPointOverviewParser.GetIncomingMovements(_html).Count.ShouldBe(1);
        }

        [Fact]
        public void NoMovements_ReturnsEmptyAndNull()
        {
            _html.LoadHtml("<div>nothing here</div>");

            MainCore.Parsers.RallyPointOverviewParser.GetIncomingMovements(_html).ShouldBeEmpty();
            MainCore.Parsers.RallyPointOverviewParser.GetIncomingHostileSeconds(_html, includeRaids: true).ShouldBeNull();
        }
    }
}
