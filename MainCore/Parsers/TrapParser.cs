namespace MainCore.Parsers
{
    // The Trapper building's own page (build.php?id=X&gid=36), captured live 2026-09-22
    // (fixture: MainCore.Test/Parsers/Trap/TrapperBuildPage.html). Two independent things live
    // on this page and must not be confused: the BUILDING'S OWN LEVEL upgrade (top section,
    // handled by the existing generic UpgradeParser/NormalBuildCommand, untouched here) and
    // building individual TRAPS (bottom form, "buildActionOverview trainUnits" - this parser).
    // Structurally this trap-build form is the same family as Barracks/Stable's troop-training
    // form (TrainTroopParser): a text input capped by an affordable-with-current-resources
    // link, plus one submit button - just with a single trap "unit" instead of a per-troop
    // lookup, so there is no troop-type parameter here.
    public static class TrapParser
    {
        // "Maximum number of traps currently possible: 64" - the CURRENT LEVEL's cap. Traps
        // above this cannot be built no matter how many resources are available; it only rises
        // when the building itself levels up (untouched by this feature).
        public static int GetMaxPossibleTraps(HtmlDocument doc)
        {
            var build = doc.GetElementbyId("build");
            if (build is null) return 0;

            var row = build.Descendants("tr").FirstOrDefault(x => x.HasClass("currentLevel"));
            if (row is null) return 0;

            var number = row.Descendants("span").FirstOrDefault(x => x.HasClass("number"));
            return number is null ? 0 : number.InnerText.ParseInt();
        }

        // "You currently have 64 traps. 0 of these traps are occupied at the moment." - the
        // first <b> is the count that matters for deciding how many more to build; the second
        // (occupied - i.e. currently holding a trapped enemy unit) is deliberately NOT read
        // here since nothing in this feature acts on it (releasing captives is a separate,
        // unrelated manual/diplomatic action).
        public static int GetCurrentTrapCount(HtmlDocument doc)
        {
            var build = doc.GetElementbyId("build");
            if (build is null) return 0;

            var paragraph = build.Descendants("p").FirstOrDefault(x => x.HasClass("traps"));
            if (paragraph is null) return 0;

            var bold = paragraph.Descendants("b").FirstOrDefault();
            return bold is null ? 0 : bold.InnerText.ParseInt();
        }

        // The trap-build "action" block (input box + affordable-amount link + submit button).
        // Unlike TrainTroopParser.GetNode there is exactly one of these per page - no troop
        // type to match against.
        private static HtmlNode? GetActionNode(HtmlDocument doc)
        {
            return doc.DocumentNode.Descendants("div")
                .FirstOrDefault(x => x.HasClass("buildActionOverview") && x.HasClass("trainUnits"));
        }

        public static HtmlNode? GetInputBox(HtmlDocument doc)
        {
            var node = GetActionNode(doc);
            if (node is null) return null;

            var cta = node.Descendants("div").FirstOrDefault(x => x.HasClass("cta"));
            if (cta is null) return null;

            return cta.Descendants("input").FirstOrDefault(x => x.HasClass("text"));
        }

        // The "Number / X" link - how many traps CAN be built right now given resources on
        // hand (same role as TrainTroopParser.GetMaxAmount for troops). 0 means genuinely
        // can't afford even one this instant, not "the form is missing" - callers must check
        // GetInputBox/GetActionNode separately if they need to tell those apart.
        public static int GetMaxAffordableNow(HtmlDocument doc)
        {
            var node = GetActionNode(doc);
            if (node is null) return 0;

            var cta = node.Descendants("div").FirstOrDefault(x => x.HasClass("cta"));
            if (cta is null) return 0;

            var link = cta.Descendants("a").FirstOrDefault();
            return link is null ? 0 : link.InnerText.ParseInt();
        }

        public static HtmlNode? GetBuildButton(HtmlDocument doc)
        {
            return doc.GetElementbyId("s1");
        }
    }
}
