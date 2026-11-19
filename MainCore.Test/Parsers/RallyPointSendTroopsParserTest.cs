using HtmlAgilityPack;
using MainCore.Parsers;

namespace MainCore.Test.Parsers
{
    public class RallyPointSendTroopsParserTest
    {
        // Regression test for the 2026-08-22 fix: the 2026-08-21 version of GetErrorMessage
        // looked for a "errorMessage" div class (copied from UpgradeParser's pattern elsewhere
        // in this codebase) which never matches on this page - the real "no village at these
        // coordinates" error is a <p class="error">, appended right after the form's closing
        // </form>. This fixture is a real capture (English client) of that exact state, reported
        // live by the user: Raid List sending troops to an empty target coordinate (35|-23).
        [Fact]
        public void GetErrorMessage_NoVillageAtCoordinates_ReturnsTheErrorText()
        {
            var doc = new HtmlDocument();
            doc.Load("Parsers/RallyPointSendTroopsParser_NoVillageError.html");

            var errorMessage = RallyPointSendTroopsParser.GetErrorMessage(doc);

            errorMessage.ShouldBe("There is no village at these coordinates.");
        }

        [Fact]
        public void IsConfirmPage_NoVillageAtCoordinates_ReturnsFalse()
        {
            // The form re-renders in place with the error appended - it never reaches
            // #confirmSendTroops. Callers must check GetErrorMessage as well as IsConfirmPage
            // (see SendTroopsCommand), or they'll wait out the full confirm-page timeout on
            // every empty-coordinate target instead of failing fast.
            var doc = new HtmlDocument();
            doc.Load("Parsers/RallyPointSendTroopsParser_NoVillageError.html");

            RallyPointSendTroopsParser.IsConfirmPage(doc).ShouldBeFalse();
        }

        // 2026-11-08: the <select> is the part of the real confirmation screen the user captured
        // (only the drop-down was provided, so the fixture holds just that, wrapped in a form).
        [Fact]
        public void GetCatapultTargetSelect_Capture_FindsTheDropDown()
        {
            var doc = new HtmlDocument();
            doc.Load("Parsers/RallyPointSendTroopsParser_CatapultTarget.html");

            RallyPointSendTroopsParser.GetCatapultTargetSelect(doc).ShouldNotBeNull();
        }

        [Theory]
        [InlineData(15, true)]  // Main Building
        [InlineData(10, true)]  // Warehouse
        [InlineData(99, true)]  // Random
        [InlineData(12, false)] // not offered (Cranny/Mason are not targetable)
        [InlineData(0, false)]
        public void HasCatapultTargetOption_Capture_MatchesTheOfferedValues(int value, bool expected)
        {
            var doc = new HtmlDocument();
            doc.Load("Parsers/RallyPointSendTroopsParser_CatapultTarget.html");

            RallyPointSendTroopsParser.HasCatapultTargetOption(doc, value).ShouldBe(expected);
        }

        [Fact]
        public void HasCatapultTargetOption_PageWithoutDropDown_ReturnsFalse()
        {
            var doc = new HtmlDocument();
            doc.Load("Parsers/RallyPointSendTroopsParser_NoVillageError.html");

            RallyPointSendTroopsParser.HasCatapultTargetOption(doc, 15).ShouldBeFalse();
        }

        [Fact]
        public void CatapultTargetEnum_ValuesMatchTheCapturedOptions()
        {
            var doc = new HtmlDocument();
            doc.Load("Parsers/RallyPointSendTroopsParser_CatapultTarget.html");

            foreach (var value in Enum.GetValues<MainCore.Enums.CatapultTargetEnums>().Where(x => x != MainCore.Enums.CatapultTargetEnums.NotSet))
            {
                RallyPointSendTroopsParser.HasCatapultTargetOption(doc, (int)value).ShouldBeTrue($"{value} = {(int)value}");
            }
        }
    }
}
