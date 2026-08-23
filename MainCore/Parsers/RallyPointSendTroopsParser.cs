namespace MainCore.Parsers
{
    // Parses the Rally Point "Send troops" tab (build.php?...&gid=16&t=2) and its confirmation
    // screen. Verified against a real page capture (English client, July 2026).
    public static class RallyPointSendTroopsParser
    {
        // ---- Step 1: the editable form ----

        public static HtmlNode? GetTroopInput(HtmlDocument doc, int troopSlot)
        {
            return doc.DocumentNode
                .Descendants("input")
                .FirstOrDefault(x => x.GetAttributeValue("name", "") == $"troop[t{troopSlot}]");
        }

        // The available count is shown as a clickable link right after the input
        // (e.g. "<a ...>258</a>"), or as "<span class="none">0</span>" when there are none.
        public static long GetAvailableTroopCount(HtmlDocument doc, int troopSlot)
        {
            var input = GetTroopInput(doc, troopSlot);
            if (input is null) return 0;

            var cell = input.ParentNode;
            if (cell is null) return 0;

            var link = cell.Descendants("a").FirstOrDefault();
            if (link is not null) return link.InnerText.ParseLong();

            return 0;
        }

        // Hero row in the "Send troops" form is slot "t11" (1-10 are the regular tribe troop
        // slots from RallyPointTroopSlots, 11 is the hero) - CONFIRMED against a real page
        // capture (Europe Qualification Tournament, English client): it's a plain text input
        // with name="troop[t11]", same markup as the other 10 slots, with the "available count"
        // (always 1, or absent if the hero isn't in the village) shown the same way via the
        // adjacent <a> link. So GetTroopInput(doc, 11) / GetAvailableTroopCount(doc, 11) already
        // work for the hero as-is - no separate parsing needed.

        public static HtmlNode? GetXInput(HtmlDocument doc)
        {
            return doc.GetElementbyId("xCoordInput");
        }

        public static HtmlNode? GetYInput(HtmlDocument doc)
        {
            return doc.GetElementbyId("yCoordInput");
        }

        // 5 = Reinforcement, 3 = Attack: Normal, 4 = Attack: Raid (verified from a real capture).
        public static HtmlNode? GetEventTypeRadio(HtmlDocument doc, int eventType)
        {
            return doc.DocumentNode
                .Descendants("input")
                .FirstOrDefault(x => x.GetAttributeValue("name", "") == "eventType"
                    && x.GetAttributeValue("value", "") == $"{eventType}");
        }

        public static HtmlNode? GetSendButton(HtmlDocument doc)
        {
            return doc.GetElementbyId("ok");
        }

        // The send form re-renders in place with a red error box instead of moving on to the
        // confirmation screen when the request can't succeed - e.g. "There is no village at
        // these coordinates." for a raid target that's been abandoned/conquered since the row
        // was set up. Retrying the exact same input will never change this outcome, so callers
        // should treat it as a permanent-for-now failure rather than waiting out the full
        // confirm-page timeout.
        //
        // CORRECTED against a real page capture (2026-08-22, English client): on this page the
        // error is NOT the "errorMessage" div class that UpgradeParser relies on elsewhere in
        // this codebase - it's <p class="error">...</p>, sitting right after the closing
        // </form>, e.g.:
        //   <p class="error">There is no village at these coordinates.</p>
        // The original 2026-08-21 fix assumed "errorMessage" (untested live) and never matched
        // this real markup, so the 180s confirm-page timeout kept firing for every empty-target
        // send instead of a fast Skip - see CHANGELOG.md 2026-08-22. Kept the old div.errorMessage
        // check too (harmless, matches nothing extra here) in case a different error condition on
        // this same page ever renders that way instead.
        public static string? GetErrorMessage(HtmlDocument doc)
        {
            var node = doc.DocumentNode
                .Descendants()
                .FirstOrDefault(x =>
                    (x.Name == "p" && x.HasClass("error"))
                    || (x.Name == "div" && x.HasClass("errorMessage")));
            return node?.InnerText?.Trim();
        }

        // ---- Step 2: the confirmation screen ----

        public static bool IsConfirmPage(HtmlDocument doc)
        {
            return doc.GetElementbyId("confirmSendTroops") is not null;
        }

        public static HtmlNode? GetConfirmButton(HtmlDocument doc)
        {
            return doc.GetElementbyId("confirmSendTroops");
        }

        // Unix timestamp of arrival, shown on the confirmation screen. Useful to log/verify
        // timing but not required to submit the movement.
        public static long? GetArrivalTimestamp(HtmlDocument doc)
        {
            var node = doc.GetElementbyId("at");
            if (node is null) return null;

            var value = node.GetAttributeValue("value", "");
            if (string.IsNullOrEmpty(value)) return null;

            return value.ParseLong();
        }
    }
}
