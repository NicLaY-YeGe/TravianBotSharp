using HtmlAgilityPack;
using MainCore.Parsers;

namespace MainCore.Test.Parsers
{
    // 2026-10-22: two real captures of /alliance/bonuses (English client, 4 bonuses, nothing selected,
    // nothing typed yet, daily limit 1,650,000, donated today 0). Capture 2 was taken in another
    // village (different stock). No capture of a FINISHED donation exists yet, so the "after
    // contribution" signals (GetDonationResponseText, donatedToday going up) are only covered with
    // hand-written snippets.
    public class AllianceBonusParserTest
    {
        private const string Capture1 = "Parsers/AllianceBonus/Bonuses_Capture1.html";
        private const string Capture2 = "Parsers/AllianceBonus/Bonuses_Capture2.html";

        // Third real capture: the form filled in (a bonus selected, amounts typed, "Sum: 321"). Copied as
        // outerHTML from DevTools, so input values are still "0" in the markup, but the Sum text and the
        // button classes show the live state: Contribute and Contribute x3 have NO "disabled" class.
        private const string Filled = "Parsers/AllianceBonus/Bonuses_Filled.html";

        private static HtmlDocument Load(string path)
        {
            var doc = new HtmlDocument();
            doc.Load(path);
            return doc;
        }

        [Theory]
        [InlineData(Capture1, 1, 2915)]
        [InlineData(Capture1, 2, 2542)]
        [InlineData(Capture1, 3, 550)]
        [InlineData(Capture1, 4, 5846)]
        [InlineData(Capture2, 1, 6856)]
        [InlineData(Capture2, 2, 8280)]
        [InlineData(Capture2, 3, 1300)]
        [InlineData(Capture2, 4, 28596)]
        public void GetMaxDonatable_ReadsTheVillageStockFromTheOnkeyupHandler(string file, int resource, long expected)
        {
            AllianceBonusParser.GetMaxDonatable(Load(file), resource).ShouldBe(expected);
        }

        [Theory]
        [InlineData(Capture1)]
        [InlineData(Capture2)]
        public void RecruitmentBonus_LabelAndRadioAreBothOnThePage(string file)
        {
            var doc = Load(file);

            AllianceBonusParser.GetRecruitmentBonusLabel(doc).ShouldNotBeNull();
            AllianceBonusParser.GetRecruitmentBonusRadio(doc).ShouldNotBeNull();
        }

        [Theory]
        [InlineData(Capture1)]
        [InlineData(Capture2)]
        public void Contribute_IsDisabledUntilABonusAndAmountAreEntered(string file)
        {
            AllianceBonusParser.IsContributeEnabled(Load(file)).ShouldBeFalse();
        }

        [Theory]
        [InlineData(Capture1)]
        [InlineData(Capture2)]
        public void DailyLimit_FreshDay_HasTheFullBudget(string file)
        {
            var doc = Load(file);

            AllianceBonusParser.IsDailyLimitReached(doc).ShouldBeFalse();
            AllianceBonusParser.GetDonatedToday(doc).ShouldBe(0);
            AllianceBonusParser.GetDailyLimitRemaining(doc).ShouldBe(1_650_000);
        }

        [Fact]
        public void FilledInForm_ContributeIsEnabled_AndTheSumIsShown()
        {
            var doc = Load(Filled);

            AllianceBonusParser.IsContributeEnabled(doc).ShouldBeTrue();
            AllianceBonusParser.GetDonateSum(doc).ShouldBe(321);
            AllianceBonusParser.GetDonationResponseText(doc).ShouldBeNull();
        }

        [Theory]
        [InlineData(Capture1)]
        [InlineData(Capture2)]
        public void EmptyForm_SumIsZero(string file)
        {
            AllianceBonusParser.GetDonateSum(Load(file)).ShouldBe(0);
        }

        // From the user's snippet taken right after a real donation of 321 (daily counter 0 -> 321).
        [Fact]
        public void DonatedTodayShown_AfterARealDonation_ReadsTheVisibleCounter()
        {
            var doc = new HtmlDocument();
            doc.LoadHtml("<div id=\"dailyContributionTitleText\" class=\"contributionText\">\u202d<span class=\"donationValueNumber\">\u202d321\u202c</span>/<span class=\"donationMaxNumber\">\u202d1,650,000\u202c</span>\u202c</div>");

            AllianceBonusParser.GetDonatedTodayShown(doc).ShouldBe(321);
        }

        [Theory]
        [InlineData(Capture1)]
        [InlineData(Capture2)]
        public void DonatedTodayShown_FreshDay_IsZero(string file)
        {
            AllianceBonusParser.GetDonatedTodayShown(Load(file)).ShouldBe(0);
        }

        [Fact]
        public void DonationResponseText_BeforeAnyDonation_IsNull()
        {
            AllianceBonusParser.GetDonationResponseText(Load(Capture1)).ShouldBeNull();
        }

        [Fact]
        public void Contribute_WithoutTheDisabledClass_IsEnabled()
        {
            var doc = new HtmlDocument();
            doc.LoadHtml("<button id=\"donate_green\" class=\"textButtonV1 green\">Contribute</button>");

            AllianceBonusParser.IsContributeEnabled(doc).ShouldBeTrue();
        }

        [Fact]
        public void DonationResponseText_WhenTheGameWritesIt_IsReturned()
        {
            var doc = new HtmlDocument();
            doc.LoadHtml("<div class=\"bonus-donation-response\"> Contribution successful. </div>");

            AllianceBonusParser.GetDonationResponseText(doc).ShouldBe("Contribution successful.");
        }

        [Fact]
        public void DailyLimit_WhenUsedUp_IsReported()
        {
            var doc = new HtmlDocument();
            doc.LoadHtml("<input type=\"hidden\" id=\"limitReached\" value=\"1\"><input type=\"hidden\" id=\"dailyLimit\" value=\"1000\"><input type=\"hidden\" id=\"donatedToday\" value=\"1000\">");

            AllianceBonusParser.IsDailyLimitReached(doc).ShouldBeTrue();
            AllianceBonusParser.GetDailyLimitRemaining(doc).ShouldBe(0);
        }
    }
}
