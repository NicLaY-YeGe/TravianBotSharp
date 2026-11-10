using System.Text.RegularExpressions;

namespace MainCore.Parsers
{
    // /alliance/bonuses page. This page is JS/AJAX-driven (Travian.Game.AllianceBonus), not a
    // plain HTML <form> post - there is no server-side "submit" URL to hit directly, the
    // donate1..4 inputs must be filled and their keyup handler fired (checkAndChange) so the
    // page's own JS re-validates the amount and un-disables the Contribute button, then that
    // button is clicked. Added 2026-09-27 for DefendDonateTask, verified against two real page
    // captures (single- and no-radio-selected states).
    public static partial class AllianceBonusParser
    {
        // donate1=Lumber, donate2=Clay, donate3=Iron, donate4=Crop (verified order from the
        // real page: the resourceSelection table lists them Lumber/Clay/Iron/Crop and its
        // rows use these exact input ids in that order).
        public static readonly Dictionary<int, string> ResourceInputIds = new()
        {
            [1] = "donate1", // Wood/Lumber
            [2] = "donate2", // Clay
            [3] = "donate3", // Iron
            [4] = "donate4", // Crop
        };

        // The current in-storage amount donatable for a resource, read from the
        // "checkAndChange(this, N, 'donate')" onkeyup handler argument (N), which the game
        // itself caps at current stock - this is the same number shown as "X / N" next to the
        // input.
        public static long? GetMaxDonatable(HtmlDocument doc, int resourceIndex)
        {
            if (!ResourceInputIds.TryGetValue(resourceIndex, out var inputId)) return null;

            var input = doc.GetElementbyId(inputId);
            if (input is null) return null;

            var onkeyup = input.GetAttributeValue("onkeyup", "");
            var match = CheckAndChangeArgRegex().Match(onkeyup);
            if (!match.Success) return null;

            return match.Groups[1].Value.ParseLong();
        }

        public static HtmlNode? GetResourceInput(HtmlDocument doc, int resourceIndex)
        {
            if (!ResourceInputIds.TryGetValue(resourceIndex, out var inputId)) return null;
            return doc.GetElementbyId(inputId);
        }

        // "TroopProductionSpeed" is the only bonus this feature ever selects (Recruitment -
        // per explicit user choice), so only that radio is looked up.
        public static HtmlNode? GetRecruitmentBonusRadio(HtmlDocument doc)
        {
            return doc.GetElementbyId("bonusTroopProductionSpeed");
        }

        public static HtmlNode? GetContributeButton(HtmlDocument doc)
        {
            return doc.GetElementbyId("donate_green");
        }

        // 2026-10-22, from two real page captures (English client). The radio inputs are plain
        // <input type="radio"> elements followed by <label for="...">; the label is the thing a
        // human clicks, and the input itself may be visually hidden by the game's CSS. Waiting for
        // the INPUT to be "displayed" is the prime suspect for the 180 s hang of the 2026-09-27
        // version, so the label is what gets clicked.
        public static HtmlNode? GetRecruitmentBonusLabel(HtmlDocument doc)
        {
            return doc.DocumentNode
                .Descendants("label")
                .FirstOrDefault(x => x.GetAttributeValue("for", "") == "bonusTroopProductionSpeed");
        }

        // The Contribute button is never really disabled: the game only adds a "disabled" CSS
        // class (the onclick then returns false), so clicking it while the class is present does
        // nothing and nothing complains. It loses the class once a bonus is selected AND a valid
        // amount is typed (checkButtonState / checkAndChange), which makes it the confirmation
        // that both steps were accepted.
        public static bool IsContributeEnabled(HtmlDocument doc)
        {
            var button = doc.GetElementbyId("donate_green");
            return button is not null && !button.HasClass("disabled");
        }

        // The "Sum:" line under the amounts (#donateSum), e.g. "‭321‬" - the game wraps the number in
        // invisible direction marks and may add thousands separators, so keep digits only. Seen as 321
        // in the real capture of a filled-in form (Bonuses_Filled.html).
        public static long GetDonateSum(HtmlDocument doc)
        {
            var text = doc.GetElementbyId("donateSum")?.InnerText ?? "";
            var digits = new string(text.Where(char.IsDigit).ToArray());
            return digits.Length == 0 ? 0 : digits.ParseLong();
        }

        // Hidden input, "1" once today's limit is used up.
        public static bool IsDailyLimitReached(HtmlDocument doc)
        {
            return doc.GetElementbyId("limitReached")?.GetAttributeValue("value", "0") == "1";
        }

        public static long GetDonatedToday(HtmlDocument doc)
        {
            return doc.GetElementbyId("donatedToday")?.GetAttributeValue("value", "0").ParseLong() ?? 0;
        }

        // 2026-10-22, confirmed by the user's snippet taken right after a real donation of 321: the
        // VISIBLE daily counter ("<span class="donationValueNumber">321</span>/1,650,000") goes up
        // after a contribution. Whether the hidden #donatedToday input follows is not known, so the
        // success check uses both. Digits only (the game wraps numbers in invisible direction marks).
        public static long GetDonatedTodayShown(HtmlDocument doc)
        {
            var node = doc.DocumentNode
                .Descendants("span")
                .FirstOrDefault(x => x.HasClass("donationValueNumber"));
            var digits = new string((node?.InnerText ?? "").Where(char.IsDigit).ToArray());
            return digits.Length == 0 ? 0 : digits.ParseLong();
        }

        // Text the game writes under the form after a contribution ("Contribution successful.").
        // Empty/null until then. NOT yet seen in a real capture of a finished donation.
        public static string? GetDonationResponseText(HtmlDocument doc)
        {
            var node = doc.DocumentNode
                .Descendants("div")
                .FirstOrDefault(x => x.HasClass("bonus-donation-response"));
            var text = node?.InnerText?.Trim();
            return string.IsNullOrEmpty(text) ? null : text;
        }

        // Account-wide daily contribution cap. Both values are plain hidden inputs' "value"
        // attribute (not display text), read directly - no thousands-separator parsing needed.
        public static long GetDailyLimitRemaining(HtmlDocument doc)
        {
            var limitNode = doc.GetElementbyId("dailyLimit");
            var donatedNode = doc.GetElementbyId("donatedToday");

            var limit = limitNode?.GetAttributeValue("value", "0").ParseLong() ?? 0;
            var donated = donatedNode?.GetAttributeValue("value", "0").ParseLong() ?? 0;

            var remaining = limit - donated;
            return remaining > 0 ? remaining : 0;
        }

        [GeneratedRegex(@"checkAndChange\(this,\s*(\d+)")]
        private static partial Regex CheckAndChangeArgRegex();
    }
}
