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

        // 2026-10-23: the Overview tab marks hostile incoming movements by the table class:
        // "troop_details inAttack" (headline "X attacks Y") and "troop_details inRaid" (headline
        // "X raids Y"). Both captured live. Until now only inAttack was read, so a village that was
        // only being raided looked "under attack" (flag on) but had no attack on the page and
        // Dodge/Defend skipped silently. The countdown markup is identical for both.
        public enum IncomingKind
        {
            Attack,
            Raid,
        }

        public sealed record IncomingMovement(IncomingKind Kind, int Seconds, string Headline);

        // Every hostile incoming movement (attack or raid) with its countdown. Rows without a
        // readable, positive countdown are left out.
        public static List<IncomingMovement> GetIncomingMovements(HtmlDocument doc)
        {
            var result = new List<IncomingMovement>();

            var tables = doc.DocumentNode
                .Descendants("table")
                .Where(x => x.GetAttributeValue("class", "").Contains("troop_details"));

            foreach (var table in tables)
            {
                var tableClass = table.GetAttributeValue("class", "");
                IncomingKind kind;
                if (tableClass.Contains("inAttack")) kind = IncomingKind.Attack;
                else if (tableClass.Contains("inRaid")) kind = IncomingKind.Raid;
                else continue;

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

                var headline = table.Descendants("td")
                    .FirstOrDefault(x => x.GetAttributeValue("class", "").Contains("troopHeadline"));
                var headlineText = headline is null ? "" : TravianCoordinateText.Normalize(headline.InnerText);

                result.Add(new IncomingMovement(kind, seconds, headlineText));
            }

            return result;
        }

        // Seconds remaining until the soonest incoming ATTACK lands (raids are ignored), if this
        // village has one. Same behaviour as before 2026-10-23.
        public static int? GetIncomingAttackSeconds(HtmlDocument doc)
            => Soonest(GetIncomingMovements(doc), includeRaids: false);

        // Seconds until the soonest incoming attack, or - when includeRaids is set - the soonest
        // attack OR raid. Null if nothing qualifies.
        public static int? GetIncomingHostileSeconds(HtmlDocument doc, bool includeRaids)
            => Soonest(GetIncomingMovements(doc), includeRaids);

        private static int? Soonest(List<IncomingMovement> movements, bool includeRaids)
        {
            int? soonest = null;
            foreach (var movement in movements)
            {
                if (movement.Kind == IncomingKind.Raid && !includeRaids) continue;
                if (soonest is null || movement.Seconds < soonest) soonest = movement.Seconds;
            }
            return soonest;
        }
    }
}
