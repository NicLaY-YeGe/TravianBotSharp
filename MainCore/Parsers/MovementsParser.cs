namespace MainCore.Parsers
{
    // Parses the "movements" widget shown in the sidebar on dorf1.php/dorf2.php - this page
    // is visited constantly during normal bot operation (every village update), which makes
    // it a far more reliable/frequent place to catch incoming attacks than waiting for a
    // dedicated village-list or rally-point refresh.
    //
    // The widget has two sections, each introduced by its own <th class="troopMovements
    // header"> row: "Incoming troops:" and "Outgoing troops:". The icon class on a data row
    // (att1/att2/... vs def1/def2/...) only encodes the movement TYPE (attack vs
    // reinforcement) - it does NOT encode direction. Direction is only known from which
    // section header the row falls under.
    //
    // BUG FOUND 2026-08-21: an earlier version scanned every <tr> in the table for an
    // "att"-prefixed icon regardless of section. A real capture showed our OWN outgoing
    // raids/attacks (e.g. RaidList sends) listed under "Outgoing troops:" with an att2 icon
    // and label "N Attacks" - identical icon convention to a real incoming attack. That
    // version therefore reported the countdown of our own outgoing raids as an incoming
    // attack, firing false-positive "under attack!" Telegram alerts while raids were simply
    // being sent out. Fixed by tracking which section each row is in and only counting rows
    // under "Incoming troops:".
    // Verified against a real page capture (English client, August 2026).
    public static class MovementsParser
    {
        // Seconds remaining until the soonest incoming ATTACK lands (ignores incoming
        // reinforcements and our own outgoing attacks/raids, which share the same icon
        // prefix but sit under a different section). Null if none incoming.
        public static int? GetIncomingAttackSeconds(HtmlDocument doc)
        {
            var table = doc.GetElementbyId("movements");
            if (table is null) return null;

            int? soonest = null;
            var insideIncomingSection = false;

            foreach (var row in table.Descendants("tr"))
            {
                var header = row.Descendants("th")
                    .FirstOrDefault(x => x.GetAttributeValue("class", "").Contains("troopMovements"));
                if (header is not null)
                {
                    // Every section (Incoming/Outgoing/...) starts with one of these header
                    // rows - re-evaluate which section we're in each time one is seen.
                    insideIncomingSection = header.InnerText.Contains("Incoming", StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                if (!insideIncomingSection) continue;

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
