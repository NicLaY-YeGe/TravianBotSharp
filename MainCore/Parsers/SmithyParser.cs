namespace MainCore.Parsers
{
    // Parses the Smithy building page (build.php?...&gid=13) - a single page, no sub-tabs.
    // Verified against a real page capture (English client, July 2026 + August 2026).
    public static class SmithyParser
    {
        // Each research block's <a class="unitZoom" onclick="...unitZoom(ID, 'Name')"> carries
        // the troop's *global* in-game id (e.g. 21 for Phalanx, 23 for Pathfinder) - and
        // TroopEnums' underlying int values were deliberately laid out to match those global ids
        // (verified against a real page capture, 2026-08-09: Phalanx=21, Pathfinder=23,
        // Druidrider=25). This is used instead of the "Improve" button's onclick to locate a
        // block, because when resources are insufficient for that troop, the game doesn't render
        // an Improve button at all (only a gold "Exchange resources" button with an unrelated
        // onclick) - matching on the Improve button made a perfectly normal "can't afford it yet"
        // state indistinguishable from "this research block isn't on the page", which caused
        // real (non-erroneous) resource-insufficient smithy visits to be treated as a hard error
        // and pause the account (see CHANGELOG.md, 2026-08-09).
        private static HtmlNode? GetResearchBlock(HtmlDocument doc, int troopSlot, TribeEnums tribe)
        {
            var slots = RallyPointTroopSlots.GetSlots(tribe);
            if (troopSlot < 1 || troopSlot > slots.Count) return null;
            var globalTroopId = (int)slots[troopSlot - 1];

            return doc.DocumentNode
                .Descendants("div")
                .Where(x => x.HasClass("research"))
                .FirstOrDefault(x => x.Descendants("a")
                    .Any(a => a.HasClass("unitZoom") &&
                              a.GetAttributeValue("onclick", "").Contains($"unitZoom({globalTroopId},")));
        }

        public static HtmlNode? GetImproveButton(HtmlDocument doc, int troopSlot, TribeEnums tribe)
        {
            var block = GetResearchBlock(doc, troopSlot, tribe);
            // HtmlAgilityPack does NOT decode HTML entities in attribute values - the real page
            // source has ampersands entity-encoded (e.g. "...&amp;t=t1&amp;checksum=..."), so a
            // literal "&t=t1&" search never matched and every Smithy upgrade silently reported
            // "not available" even when the Improve button was actually present (see CHANGELOG.md,
            // 2026-08-10). Decode entities before comparing.
            return block?.Descendants("button")
                .FirstOrDefault(b => HtmlEntity.DeEntitize(b.GetAttributeValue("onclick", "")).Contains($"&t=t{troopSlot}&"));
        }

        public static int GetLevel(HtmlDocument doc, int troopSlot, TribeEnums tribe)
        {
            var block = GetResearchBlock(doc, troopSlot, tribe);
            var span = block?.Descendants("span").FirstOrDefault(x => x.HasClass("level"));
            if (span is null) return -1;
            return span.InnerText.ParseInt();
        }

        // True when the troop's research block isn't on the page at all - wrong slot for this
        // tribe, this troop's unit-producing building (Barracks/Stable/Workshop) not built yet
        // so the game doesn't offer its research at all, or the page didn't parse as expected.
        // Distinct from IsUnavailable (block found, but no Improve button - smithy too low,
        // already maxed, not enough resources yet, or a different research is already running),
        // which is an expected, everyday state and not worth a warning.
        public static bool IsResearchBlockMissing(HtmlDocument doc, int troopSlot, TribeEnums tribe)
        {
            return GetResearchBlock(doc, troopSlot, tribe) is null;
        }

        // True when this troop can't be upgraded right now (smithy level too low, already
        // maxed, not enough resources yet, or a different research is already in progress).
        public static bool IsUnavailable(HtmlDocument doc, int troopSlot, TribeEnums tribe)
        {
            return GetImproveButton(doc, troopSlot, tribe) is null;
        }

        // Seconds remaining until the currently-running research finishes, if any.
        public static int? GetOngoingResearchSecondsRemaining(HtmlDocument doc)
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
