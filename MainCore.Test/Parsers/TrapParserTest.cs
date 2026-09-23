using HtmlAgilityPack;
using MainCore.Parsers;

namespace MainCore.Test.Parsers
{
    // Fixture: Parsers/Trap/TrapperBuildPage.html - trimmed from a REAL capture of the
    // Trapper (gid=36) build page (2026-09-22), only the "Number / X" link's value changed
    // from the captured 0 to 37 so a real "can afford some" case is covered too.
    public class TrapParserTest
    {
        private static HtmlDocument Load()
        {
            var doc = new HtmlDocument();
            doc.Load("Parsers/Trap/TrapperBuildPage.html");
            return doc;
        }

        [Fact]
        public void GetMaxPossibleTraps_ReadsTheCurrentLevelsCap()
        {
            TrapParser.GetMaxPossibleTraps(Load()).ShouldBe(64);
        }

        [Fact]
        public void GetCurrentTrapCount_ReadsTheFirstBoldNumber_NotTheOccupiedCount()
        {
            // The paragraph text is "You currently have 64 traps. 0 of these traps are
            // occupied" - both numbers are 64/0 in the real capture, so this alone wouldn't
            // catch reading the wrong <b>; GetMaxAffordableNow's own test (a different number,
            // 37) is what actually proves the parser is looking in the right places overall.
            TrapParser.GetCurrentTrapCount(Load()).ShouldBe(64);
        }

        [Fact]
        public void GetMaxAffordableNow_ReadsTheNumberSlashLink()
        {
            TrapParser.GetMaxAffordableNow(Load()).ShouldBe(37);
        }

        [Fact]
        public void GetInputBox_FindsTheTrapCountTextInput()
        {
            var input = TrapParser.GetInputBox(Load());

            input.ShouldNotBeNull();
            input!.GetAttributeValue("name", "").ShouldBe("t911");
        }

        [Fact]
        public void GetBuildButton_FindsTheSubmitButtonById()
        {
            var button = TrapParser.GetBuildButton(Load());

            button.ShouldNotBeNull();
            button!.GetAttributeValue("name", "").ShouldBe("s1");
        }

        [Fact]
        public void AllReaders_ReturnEmptyOrZero_OnAPageWithoutTheTrapperBuild()
        {
            var doc = new HtmlDocument();
            doc.LoadHtml("<div id=\"content\"></div>");

            TrapParser.GetMaxPossibleTraps(doc).ShouldBe(0);
            TrapParser.GetCurrentTrapCount(doc).ShouldBe(0);
            TrapParser.GetMaxAffordableNow(doc).ShouldBe(0);
            TrapParser.GetInputBox(doc).ShouldBeNull();
            TrapParser.GetBuildButton(doc).ShouldBeNull();
        }
    }
}
