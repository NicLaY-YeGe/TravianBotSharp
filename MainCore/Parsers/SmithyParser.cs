namespace MainCore.Parsers
{
    // Parses the Smithy building page (build.php?...&gid=13) - a single page, no sub-tabs.
    // Verified against a real page capture (English client, July 2026).
    public static class SmithyParser
    {
        // Each troop's research block is a <div class="research"> containing an "Improve"
        // button whose onclick has "&t=t{slot}&" in the URL it navigates to.
        private static HtmlNode? GetResearchBlock(HtmlDocument doc, int troopSlot)
        {
            return doc.DocumentNode
                .Descendants("div")
                .Where(x => x.GetAttributeValue("class", "") == "research")
                .FirstOrDefault(x => x.Descendants("button")
                    .Any(b => b.GetAttributeValue("onclick", "").Contains($"&t=t{troopSlot}&")));
        }

        public static HtmlNode? GetImproveButton(HtmlDocument doc, int troopSlot)
        {
            var block = GetResearchBlock(doc, troopSlot);
            return block?.Descendants("button")
                .FirstOrDefault(b => b.GetAttributeValue("onclick", "").Contains($"&t=t{troopSlot}&"));
        }

        public static int GetLevel(HtmlDocument doc, int troopSlot)
        {
            var block = GetResearchBlock(doc, troopSlot);
            var span = block?.Descendants("span").FirstOrDefault(x => x.HasClass("level"));
            if (span is null) return -1;
            return span.InnerText.ParseInt();
        }

        // True when this troop can't be upgraded right now (smithy level too low, already
        // maxed, or a different research is already in progress).
        public static bool IsUnavailable(HtmlDocument doc, int troopSlot)
        {
            return GetImproveButton(doc, troopSlot) is null;
        }
    }
}
