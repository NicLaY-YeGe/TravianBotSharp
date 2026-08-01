namespace MainCore.Parsers
{
    // Parses the Town Hall page (build.php?...&gid=24) - single page, no sub-tabs.
    // Verified against a real page capture (English client, July 2026).
    public static class TownHallParser
    {
        // Each celebration option is a <div class="research"> with a title containing the
        // celebration's name and a "Hold" button - same layout pattern as the Smithy page.
        private static HtmlNode? GetResearchBlock(HtmlDocument doc, string celebrationName)
        {
            return doc.DocumentNode
                .Descendants("div")
                .Where(x => x.GetAttributeValue("class", "") == "research")
                .FirstOrDefault(x =>
                {
                    var title = x.Descendants("div").FirstOrDefault(t => t.HasClass("title"));
                    return title is not null && title.InnerText.Contains(celebrationName, StringComparison.OrdinalIgnoreCase);
                });
        }

        public static HtmlNode? GetHoldButton(HtmlDocument doc, string celebrationName)
        {
            var block = GetResearchBlock(doc, celebrationName);
            return block?.Descendants("button").FirstOrDefault(b => b.GetAttributeValue("value", "") == "Hold");
        }

        // True when this celebration can't be started right now (not enough resources,
        // town hall level too low for "Great celebration", or one is already running).
        public static bool IsUnavailable(HtmlDocument doc, string celebrationName)
        {
            return GetHoldButton(doc, celebrationName) is null;
        }

        // Seconds remaining until the currently-running celebration finishes, if any.
        public static int? GetOngoingCelebrationSecondsRemaining(HtmlDocument doc)
        {
            var table = doc.DocumentNode
                .Descendants("table")
                .FirstOrDefault(x => x.HasClass("under_progress"));
            if (table is null) return null;

            var timer = table.Descendants("span")
                .FirstOrDefault(x => x.GetAttributeValue("class", "") == "timer");
            if (timer is null) return null;

            var seconds = timer.GetAttributeValue("data-value", "").ParseInt();
            return seconds > 0 ? seconds : null;
        }
    }
}
