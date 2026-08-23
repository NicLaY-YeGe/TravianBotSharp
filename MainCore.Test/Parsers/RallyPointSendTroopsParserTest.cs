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
    }
}
