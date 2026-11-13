using MainCore.Parsers;

namespace MainCore.Test.Parsers
{
    // Fixtures are the Rally Point "Overview" tab of two real accounts (2026-11-11):
    //   TroopsStationed.html - TS-50 Gaul village, no movements, 319 Phalanx at home and 24 + 33 + 55 Phalanx stationed in
    //                          three other villages ("Reinforcement for ...");
    //   TroopsMoving.html    - TS-12 village A_1201 (Rally Point 18): 10 returning + 10 outgoing-raid tables although both
    //                          headings say (12) - the page shows 10 per section and offers "Show all"; at home the own
    //                          troops plus 291 Phalanx of the player's other village A_1202.
    public class RallyPointTroopsParserTest : BaseParser
    {
        private const string Stationed = "Parsers/RallyPointOverview/TroopsStationed.html";
        private const string Moving = "Parsers/RallyPointOverview/TroopsMoving.html";

        [Fact]
        public void Stationed_HomeIsTheOwnTroopsTable()
        {
            _html.Load(Stationed);
            var troops = RallyPointTroopsParser.Get(_html)!;

            string.Join(",", troops.Home).ShouldBe("319,12,0,59,30,0,0,0,0,0,1");
            troops.HasOwnTroopsTable.ShouldBeTrue();
        }

        [Fact]
        public void Stationed_TroopsInOtherVillagesAreSummed()
        {
            _html.Load(Stationed);
            var troops = RallyPointTroopsParser.Get(_html)!;

            string.Join(",", troops.Stationed).ShouldBe("112,0,0,0,0,0,0,0,0,0,0");
            troops.Moving.Sum().ShouldBe(0);
            troops.Returning.Sum().ShouldBe(0);
            troops.Reinforcement.Sum().ShouldBe(0);
            troops.IncomingTruncated.ShouldBeFalse();
            troops.OutgoingTruncated.ShouldBeFalse();
        }

        [Fact]
        public void Names_AreTheUnitIconsAltTexts()
        {
            _html.Load(Stationed);
            var troops = RallyPointTroopsParser.Get(_html)!;

            troops.Names[0].ShouldBe("Phalanx");
            troops.Names[3].ShouldBe("Theutates Thunder");
            troops.Names[10].ShouldBe("Hero");
        }

        [Fact]
        public void Moving_HomeAndOwnVillageReinforcementAreSeparate()
        {
            _html.Load(Moving);
            var troops = RallyPointTroopsParser.Get(_html)!;

            string.Join(",", troops.Home).ShouldBe("1033,288,200,128,79,14,25,17,0,0,1");
            string.Join(",", troops.Reinforcement).ShouldBe("291,0,0,0,0,0,0,0,0,0,0");
        }

        [Fact]
        public void Moving_OutgoingRaidsAndReturnsAreSummedPerSlot()
        {
            _html.Load(Moving);
            var troops = RallyPointTroopsParser.Get(_html)!;

            // 10 outRaid tables of Swordsman: 48+188+88+36+70+84+28+84+35+44
            troops.Moving[1].ShouldBe(705);
            // 10 inReturn tables: 63+51+47+83+22+30+54+57+56+124
            troops.Returning[1].ShouldBe(587);
            troops.Stationed.Sum().ShouldBe(0);
        }

        [Fact]
        public void Moving_BothSectionsAreReportedAsTruncated()
        {
            _html.Load(Moving);
            var troops = RallyPointTroopsParser.Get(_html)!;

            troops.IncomingTruncated.ShouldBeTrue();
            troops.OutgoingTruncated.ShouldBeTrue();
        }

        [Fact]
        public void PageWithoutTheOverviewContainer_GivesNull()
        {
            _html.LoadHtml("<div class=\"data\"></div>");
            RallyPointTroopsParser.Get(_html).ShouldBeNull();
        }
    }
}
