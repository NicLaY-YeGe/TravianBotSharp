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
