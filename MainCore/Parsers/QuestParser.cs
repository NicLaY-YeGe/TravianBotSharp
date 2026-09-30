namespace MainCore.Parsers
{
    public static class QuestParser
    {
        public static HtmlNode GetQuestMaster(HtmlDocument doc)
        {
            var questmasterButton = doc.GetElementbyId("questmasterButton");
            return questmasterButton;
        }

        public static bool IsQuestClaimable(HtmlDocument doc)
        {
            var questmasterButton = GetQuestMaster(doc);
            if (questmasterButton is null) return false;
            var newQuestSpeechBubble = questmasterButton
                .Descendants("div")
                .Any(x => x.HasClass("newQuestSpeechBubble"));
            return newQuestSpeechBubble;
        }

        public static HtmlNode? GetQuestCollectButton(HtmlDocument doc)
        {
            var taskOverviewTable = doc.DocumentNode
                .Descendants("div")
                .FirstOrDefault(x => x.HasClass("taskOverview"));

            if (taskOverviewTable is null) return null;

            var collectButton = taskOverviewTable
                .Descendants("button")
                .FirstOrDefault(x => x.HasClass("collect") && !x.HasClass("disabled"));
            return collectButton;
        }

        // 2026-09-29, real bug: the task list within a quest tab is paginated (see
        // QuestPage.html - 3 pages for that village), and ClaimQuestCommand used to only ever
        // look at the page it happened to land on. A claimable quest sitting on page 2 or 3
        // was never reached, the "new quest" bubble (IsQuestClaimable) never cleared, and
        // UpdateQuestCommand kept re-adding ClaimQuestTask forever - see CHANGELOG. Null when
        // there's no pagination at all, or the "forward" button is disabled (already on the
        // last page).
        public static HtmlNode? GetNextPageButton(HtmlDocument doc)
        {
            var pagination = doc.DocumentNode
                .Descendants("div")
                .FirstOrDefault(x => x.HasClass("pagination"));
            if (pagination is null) return null;

            var forward = pagination
                .Descendants("button")
                .FirstOrDefault(x => x.HasClass("forward"));
            if (forward is null || forward.HasClass("disabled")) return null;

            return forward;
        }

        public static bool IsQuestPage(HtmlDocument doc)
        {
            var table = doc.DocumentNode
                .Descendants("div")
                .Any(x => x.HasClass("tasks") && x.HasClass("tasksVillage"));
            return table;
        }
    }
}