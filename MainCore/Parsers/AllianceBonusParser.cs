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
