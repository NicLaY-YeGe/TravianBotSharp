namespace MainCore.Parsers
{
    // Reads the Cranny (Sığınak/Hideout, gid=23) building page's own "protected amount"
    // figure - same for all 4 resource types (per official game description: "the cranny
    // can hold N of each resource"). Added 2026-09-27 for DefendDonateTask (see
    // CLAUDE.md/PROJECT_CONTEXT.md).
    //
    // Verified from two real page captures:
    //  - Single-Cranny village: only a `tr.currentLevel` row exists -
    //    "Units per resource hidden by this cranny: <N> units".
    //  - Multi-Cranny village (several Cranny buildings in the same village): the page ALSO
    //    shows a `tr.overall` row above it - "Units per resource hidden by all crannies: <N>
    //    units" - which is the game's own already-summed total across every Cranny building
    //    in the village. Per explicit user instruction, we read THIS total when present
    //    instead of visiting every Cranny building and summing them ourselves; when it's
    //    absent (single Cranny), `currentLevel`'s value already IS the village's total.
    public static class CrannyParser
    {
        public static long GetProtectedAmount(HtmlDocument doc)
        {
            var overallRow = doc.DocumentNode
                .Descendants("tr")
                .FirstOrDefault(x => x.HasClass("overall"));
            if (overallRow is not null)
            {
                var overallValue = GetNumberSpan(overallRow);
                if (overallValue is not null) return overallValue.Value;
            }

            var currentLevelRow = doc.DocumentNode
                .Descendants("tr")
                .FirstOrDefault(x => x.HasClass("currentLevel"));
            if (currentLevelRow is not null)
            {
                var currentValue = GetNumberSpan(currentLevelRow);
                if (currentValue is not null) return currentValue.Value;
            }

            return 0;
        }

        private static long? GetNumberSpan(HtmlNode row)
        {
            var span = row.Descendants("span")
                .FirstOrDefault(x => x.HasClass("number"));
            if (span is null) return null;
            return span.InnerText.ParseLong();
        }
    }
}
