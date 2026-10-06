namespace MainCore.Parsers
{
    // Daily Quests dialog (the top-bar "a.dailyQuests" icon, accesskey 7). Markup taken from two
    // real captures (2026-10-05/06): the icon carries <div class="indicator">!</div> ONLY while
    // something can be collected (hidden state: class "indicator hidden"); inside the opened
    // dialog (#dailyQuests) every milestone is <div class="rewardImage reward25|50|75|100 X">
    // with X = locked / achieved (reached, NOT yet collected) / completed (collected), and the
    // "Collect rewards" button (button.collectRewards) is disabled while nothing is collectable.
    // Language independent - classes only.
    public static class DailyQuestParser
    {
        public static HtmlNode? GetOpenLink(HtmlDocument doc)
        {
            return doc.DocumentNode
                .Descendants("a")
                .FirstOrDefault(x => x.HasClass("dailyQuests"));
        }

        public static bool IsClaimable(HtmlDocument doc)
        {
            var link = GetOpenLink(doc);
            if (link is null) return false;

            var indicator = link
                .Descendants("div")
                .FirstOrDefault(x => x.HasClass("indicator"));
            return indicator is not null && !indicator.HasClass("hidden");
        }

        public static bool IsDialogOpen(HtmlDocument doc)
        {
            return doc.GetElementbyId("dailyQuests") is not null;
        }

        // Null when the dialog is not open or the button is disabled (nothing to collect).
        public static HtmlNode? GetCollectButton(HtmlDocument doc)
        {
            var dialog = doc.GetElementbyId("dailyQuests");
            if (dialog is null) return null;

            var button = dialog
                .Descendants("button")
                .FirstOrDefault(x => x.HasClass("collectRewards"));
            if (button is null) return null;
            if (button.Attributes.Contains("disabled")) return null;
            return button;
        }

        public static bool HasAchievedReward(HtmlDocument doc)
        {
            var dialog = doc.GetElementbyId("dailyQuests");
            if (dialog is null) return false;

            return dialog
                .Descendants("div")
                .Any(x => x.HasClass("rewardImage") && x.HasClass("achieved"));
        }

        public static HtmlNode? GetCloseButton(HtmlDocument doc)
        {
            return doc.DocumentNode
                .Descendants("div")
                .FirstOrDefault(x => x.HasClass("dialogCancelButton")
                    && x.Ancestors("div").Any(a => a.HasClass("dailyQuestsDialog")));
        }
    }
}
