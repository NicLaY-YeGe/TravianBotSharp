namespace MainCore.Parsers
{
    // Parses the "Incoming troops" widget shown in the sidebar on dorf1.php/dorf2.php -
    // this page is visited constantly during normal bot operation (every village update),
    // which makes it a far more reliable/frequent place to catch incoming attacks than
    // waiting for a dedicated village-list or rally-point refresh.
    // Verified against a real page capture (English client, July 2026).
    public static class MovementsParser
    {
        // Seconds remaining until the soonest incoming ATTACK lands (ignores incoming
        // reinforcements, which use a different icon prefix). Null if none incoming.
        public static int? GetIncomingAttackSeconds(HtmlDocument doc)
        {
            var table = doc.GetElementbyId("movements");
            if (table is null) return null;

            int? soonest = null;

            foreach (var row in table.Descendants("tr"))
            {
                var icon = row.Descendants("img").FirstOrDefault();
                var iconClass = icon?.GetAttributeValue("class", "") ?? "";
                // attack rows use an icon class starting with "att" (att1, att2, ...),
                // incoming reinforcements use "def" - only count attacks.
                if (!iconClass.StartsWith("att")) continue;

                var timer = row.Descendants("span").FirstOrDefault(x => x.GetAttributeValue("class", "") == "timer");
                if (timer is null) continue;

                var seconds = timer.GetAttributeValue("data-value", "").ParseInt();
                if (seconds <= 0) continue;

                if (soonest is null || seconds < soonest) soonest = seconds;
            }

            return soonest;
        }
    }
}
