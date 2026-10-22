namespace MainCore.Parsers
{
    public static class BuildingTabParser
    {
        public static HtmlNode? GetNavigationBar(HtmlDocument doc)
        {
            var navigationBar = doc.DocumentNode
             .Descendants("div")
             .FirstOrDefault(x => x.HasClass("contentNavi") && x.HasClass("subNavi"));
            if (navigationBar is null) return null;
            return navigationBar;
        }

        private static IEnumerable<HtmlNode> GetTabs(HtmlDocument doc)
        {
            var navigationBar = GetNavigationBar(doc);
            if (navigationBar is null) return [];
            var tabs = navigationBar
                .Descendants("a")
                .Where(x => x.HasClass("tabItem"));
            return tabs;
        }

        public static int CountTab(HtmlDocument doc)
        {
            var count = GetTabs(doc)
                .Count();
            return count;
        }

        public static HtmlNode GetTab(HtmlDocument doc, int index)
        {
            var tab = GetTabs(doc)
                .ElementAt(index);
            return tab;
        }

        // 2026-10-21: null-returning variant for code that looks at a LIVE page (SwitchTabCommand's
        // wait predicate). Right after a click the page is mid-navigation - the old one unloading,
        // the new one not rendered yet - and has no tab bar at all; GetTab's ElementAt then threw
        // ArgumentOutOfRangeException (live log 13:01:43, TS12) and killed the wait.
        public static HtmlNode? TryGetTab(HtmlDocument doc, int index)
        {
            if (index < 0) return null;
            return GetTabs(doc).Skip(index).FirstOrDefault();
        }

        public static bool IsTabActive(HtmlNode node)
        {
            return node.HasClass("active");
        }
    }
}