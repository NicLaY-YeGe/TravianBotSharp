namespace MainCore.Parsers
{
    // Parses the Map page (karte.php) coordinate-jump form and the "Found new village" link
    // that appears once a valid, foundable coordinate is entered. Added 2026-08-12/13 for
    // Auto Settle (see CLAUDE.md/PROJECT_CONTEXT.md §5k). The coordinate inputs use the SAME
    // id pattern as the Rally Point "Send troops" form (xCoordInput/yCoordInput), with a "Map"
    // suffix - confirmed against a real page capture.
    public static class MapParser
    {
        public static HtmlNode? GetXInput(HtmlDocument doc) => doc.GetElementbyId("xCoordInputMap");

        public static HtmlNode? GetYInput(HtmlDocument doc) => doc.GetElementbyId("yCoordInputMap");

        // Only appears once a foundable coordinate (empty, within settler range) is entered -
        // a null return means either the coordinates haven't been entered yet, or that tile
        // isn't currently foundable. Its href embeds eventType=10 and the target's internal
        // map id; we don't compute that id ourselves, we just read and click whatever the
        // page gives us (same "read the resulting element, don't calculate it" approach
        // already used for the abort/confirm buttons elsewhere in this project).
        public static HtmlNode? GetFoundNewVillageLink(HtmlDocument doc)
        {
            return doc.DocumentNode
                .Descendants("a")
                .FirstOrDefault(x => x.GetAttributeValue("href", "").Contains("eventType=10")
                    && x.InnerText.Contains("Found new village", StringComparison.OrdinalIgnoreCase));
        }
    }

    // Parses the Settle confirmation screen reached from MapParser.GetFoundNewVillageLink.
    // Structurally different from the Rally Point attack/reinforcement confirm screen (see
    // CLAUDE.md/CHANGELOG.md §5k, 2026-08-13): x/y/eventType are embedded in the URL rather
    // than hidden form fields, troops[0][t10]=3 is fixed, and the checksum is the submit
    // button's own "value" attribute rather than a separately JS-set hidden input - so a
    // plain click is enough, no pre-click checksum step needed.
    public static class SettleConfirmParser
    {
        public static bool IsSettleConfirmPage(HtmlDocument doc)
        {
            return doc.DocumentNode
                .Descendants("form")
                .Any(x => x.GetAttributeValue("class", "").Contains("settleVillageForm"));
        }

        public static HtmlNode? GetSettleButton(HtmlDocument doc) => doc.GetElementbyId("checksum");
    }
}
