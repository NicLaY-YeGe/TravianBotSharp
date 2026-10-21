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

        // Finds the movement row whose HEADLINE mentions the target coordinates and returns
        // its abort/cancel button, if the movement is still within its cancel window. Used for
        // Dodge's attack-type sends (2026-08-13 evolution) and any other outgoing movement
        // whose target isn't one of our own named villages, so GetRecallButton's
        // "reinforces {name}" match doesn't apply.
        //
        // IMPORTANT (bug found 2026-08-13, see CHANGELOG.md/PROJECT_CONTEXT.md §5m): an
        // earlier version of this matched the row's <th class="coords"> cell instead - but a
        // real "outgoing attack" capture showed that cell holds the SOURCE village's
        // coordinates, not the target's. Matching against it silently matched the wrong
        // village's dodge attempt whenever more than one village was dodging at once (or
        // simply never matched). The headline text itself is the reliable source: Travian
        // prints the raw target coordinates there for movements without a named destination
        // village. CONFIRMED live 2026-10-20 for raids/attacks on an unoccupied oasis
        // ("Raid unoccupied oasis (35|-5)"), but the coordinates are wrapped in invisible bidi
        // characters and use U+2212 as the minus sign - hence the normalisation below (the
        // earlier plain Contains never matched, so Recall never found the button).
        public static HtmlNode? GetOutgoingAttackAbortButton(HtmlDocument doc, int targetX, int targetY)
        {
            var tables = doc.DocumentNode
                .Descendants("table")
                .Where(x => x.GetAttributeValue("class", "").Contains("troop_details"));

            foreach (var table in tables)
            {
                var headline = table.Descendants("td")
                    .FirstOrDefault(x => x.GetAttributeValue("class", "").Contains("troopHeadline"));
                if (headline is null) continue;

                // 2026-10-20: the raw text carries invisible bidi characters and U+2212 minus
                // signs, so it is normalised first (see TravianCoordinateText).
                if (!TravianCoordinateText.ContainsCoordinate(headline.InnerText, targetX, targetY)) continue;

                var abortDiv = table.Descendants("div")
                    .FirstOrDefault(x => x.GetAttributeValue("class", "") == "abort");
                var button = abortDiv?.Descendants("button").FirstOrDefault();
                if (button is not null) return button;
            }

            return null;
        }

        // Diagnostics for RecallTroopCommand: the normalised headline of every outgoing movement
        // on the page, so a miss can be explained from the log alone.
        public static List<string> GetOutgoingMovementHeadlines(HtmlDocument doc)
        {
            return doc.DocumentNode
                .Descendants("table")
                .Where(x => x.GetAttributeValue("class", "").Contains("troop_details"))
                .Select(t => t.Descendants("td").FirstOrDefault(x => x.GetAttributeValue("class", "").Contains("troopHeadline")))
                .Where(h => h is not null)
                .Select(h => TravianCoordinateText.Normalize(h!.InnerText))
                .ToList();
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

                // 2026-10-18: the Rally Point Overview timer carries its seconds in a plain
                // `value` attribute (`<span class="timer" counting="down" value="424">`), NOT
                // `data-value` like the dorf1/dorf2 #movements widget. Real incoming-attack
                // capture confirmed this; reading only `data-value` made every read return
                // null, so Dodge/Defend skipped themselves. `data-value` kept as a fallback.
                var raw = timer.GetAttributeValue("value", "");
                if (string.IsNullOrWhiteSpace(raw)) raw = timer.GetAttributeValue("data-value", "");
                var seconds = raw.ParseInt();
                if (seconds <= 0) continue;

                if (soonest is null || seconds < soonest) soonest = seconds;
            }

            return soonest;
        }
    }
}
