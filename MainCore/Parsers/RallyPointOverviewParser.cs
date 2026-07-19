namespace MainCore.Parsers
{
    // Parses the Rally Point "Overview" tab (build.php?...&gid=16&t=1), specifically the
    // recall ("bring troops home") button on one of our own outgoing reinforcement movements.
    // Verified against a real page capture (English client, July 2026).
    public static class RallyPointOverviewParser
    {
        // Finds the movement row whose headline is "... reinforces {targetVillageName}" and
        // returns its recall button, if one is currently shown. The button is only present
        // while the movement is recallable (Travian hides it briefly around the exact
        // arrival moment) - a null return just means "not recallable right now".
        public static HtmlNode? GetRecallButton(HtmlDocument doc, string targetVillageName)
        {
            var tables = doc.DocumentNode
                .Descendants("table")
                .Where(x => x.GetAttributeValue("class", "").Contains("troop_details"));

            foreach (var table in tables)
            {
                var headline = table.Descendants("td")
                    .FirstOrDefault(x => x.GetAttributeValue("class", "").Contains("troopHeadline"));
                if (headline is null) continue;

                var text = headline.InnerText;
                if (!text.Contains($"reinforces {targetVillageName}", StringComparison.OrdinalIgnoreCase)) continue;

                var abortDiv = table.Descendants("div")
                    .FirstOrDefault(x => x.GetAttributeValue("class", "") == "abort");
                var button = abortDiv?.Descendants("button").FirstOrDefault();
                if (button is not null) return button;
            }

            return null;
        }

        // Seconds remaining until the soonest incoming attack lands, if this village has one.
        // Reads the countdown from the "inAttack" movement row(s) on this same Overview tab.
        public static int? GetIncomingAttackSeconds(HtmlDocument doc)
        {
            var tables = doc.DocumentNode
                .Descendants("table")
                .Where(x => x.GetAttributeValue("class", "").Contains("inAttack"));

            int? soonest = null;

            foreach (var table in tables)
            {
                var timer = table.Descendants("span")
                    .FirstOrDefault(x => x.GetAttributeValue("class", "") == "timer");
                if (timer is null) continue;

                var seconds = timer.GetAttributeValue("data-value", "").ParseInt();
                if (seconds <= 0) continue;

                if (soonest is null || seconds < soonest) soonest = seconds;
            }

            return soonest;
        }
    }
}
