namespace MainCore.Parsers
{
    public static class AdventureParser
    {
        public static TimeSpan GetAdventureDuration(HtmlDocument doc)
        {
            var heroAdventure = doc.GetElementbyId("heroAdventure");
            var timer = heroAdventure
                .Descendants("span")
                .FirstOrDefault(x => x.HasClass("timer"));
            if (timer is null) return TimeSpan.Zero;

            var seconds = timer.GetAttributeValue("value", 0);
            return TimeSpan.FromSeconds(seconds);
        }

        public static bool IsAdventurePage(HtmlDocument doc)
        {
            var table = doc.DocumentNode
                .Descendants("table")
                .Any(x => x.HasClass("adventureList"));
            return table;
        }

        public static HtmlNode? GetHeroAdventureButton(HtmlDocument doc)
        {
            var adventureButton = doc.DocumentNode
                .Descendants("a")
                .FirstOrDefault(x => x.HasClass("adventure") && x.HasClass("round"));
            return adventureButton;
        }

        public static bool CanStartAdventure(HtmlDocument doc)
        {
            var heroStatus = doc.DocumentNode
                .Descendants("div")
                .FirstOrDefault(x => x.HasClass("heroStatus"));
            if (heroStatus is null) return false;
            var heroHome = heroStatus.Descendants("i")
                .Any(x => x.HasClass("heroHome"));
            if (!heroHome) return false;

            var adventureButton = GetHeroAdventureButton(doc);
            if (adventureButton is null) return false;

            var adventureAvailabe = adventureButton.Descendants("div")
                .Any(x => x.HasClass("content"));
            return adventureAvailabe;
        }

        public static HtmlNode? GetAdventureButton(HtmlDocument doc)
        {
            var adventureTable = doc.GetElementbyId("heroAdventure");
            if (adventureTable is null) return null;

            var adventureTableBody = adventureTable
                .Descendants("tbody")
                .FirstOrDefault();
            if (adventureTableBody is null) return null;

            var adventureTableBodyRow = adventureTableBody
                .Descendants("tr")
                .FirstOrDefault();
            if (adventureTableBodyRow is null) return null;

            var startAdventureButton = adventureTableBodyRow
                .Descendants("button")
                .FirstOrDefault();
            return startAdventureButton;
        }

        // 2026-11-04 (live log 10:03-10:18 + the user's screenshot): the hero's home village lost its Rally Point,
        // the game showed "The hero's home village needs a rally point in order to send the hero" and greyed the
        // Explore button out. A greyed button is only a CSS class (CLAUDE.md rule 11), so the click "worked" and the bot
        // waited 180 s for the Continue button, four times, until the account was paused.
        public static bool IsExploreDisabled(HtmlNode button)
        {
            return button.Attributes.Contains("disabled") || button.HasClass("disabled");
        }

        // Best-effort text of the game's red warning on the adventure dialog (log/diagnostics only - never used to
        // decide anything). Looks for an element whose class says error/warning, then for the known rally point text.
        public static string? GetBlockedMessage(HtmlDocument doc)
        {
            var flagged = doc.DocumentNode
                .Descendants()
                .Where(x => x.NodeType == HtmlNodeType.Element)
                .Where(x => ClassContains(x, "error") || ClassContains(x, "warning"))
                .Select(x => CleanText(x.InnerText))
                .FirstOrDefault(x => x.Length > 0 && x.Length <= 200);
            if (flagged is not null) return flagged;

            return doc.DocumentNode
                .Descendants()
                .Where(x => x.NodeType == HtmlNodeType.Element && x.ChildNodes.Count <= 3)
                .Select(x => CleanText(x.InnerText))
                .FirstOrDefault(x => x.Length > 0 && x.Length <= 200 && x.Contains("rally point", StringComparison.OrdinalIgnoreCase));
        }

        private static bool ClassContains(HtmlNode node, string part)
        {
            var value = node.GetAttributeValue("class", "");
            return value.Contains(part, StringComparison.OrdinalIgnoreCase);
        }

        private static string CleanText(string text)
        {
            return System.Net.WebUtility.HtmlDecode(text).Trim();
        }

        public static HtmlNode? GetContinueButton(HtmlDocument doc)
        {
            var continueButton = doc.DocumentNode
                .Descendants("button")
                .FirstOrDefault(x => x.HasClass("continue"));
            return continueButton;
        }

        public static string GetAdventureInfo(HtmlNode node)
        {
            // adventureTableBodyRow/td/buton
            var trNode = node.ParentNode.ParentNode;
            var difficult = GetAdventureDifficult(trNode);
            var coordinates = GetAdventureCoordinates(trNode);

            return $"{difficult} - {coordinates}";
        }

        private static string GetAdventureDifficult(HtmlNode node)
        {
            var tdList = node.Descendants("td").ToArray();
            if (tdList.Length < 3) return "unknown";
            var iconDifficulty = tdList[3].FirstChild;
            return iconDifficulty.GetAttributeValue("alt", "unknown");
        }

        private static string GetAdventureCoordinates(HtmlNode node)
        {
            var tdList = node.Descendants("td").ToArray();
            if (tdList.Length < 2) return "[~|~]";
            return tdList[1].InnerText;
        }
    }
}