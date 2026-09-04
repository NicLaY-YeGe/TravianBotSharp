namespace MainCore.Parsers
{
    public static class CookieConsentParser
    {
        // Consent Manager Provider (CMP) "Consent to Cookies and Data Processing" overlay.
        // It can appear on top of any page at any time and, because ChromeBrowser.Click()
        // clicks by screen coordinate rather than by DOM node, it silently swallows clicks
        // meant for whatever real button happens to sit underneath it — with no exception,
        // so the calling command thinks the click succeeded.
        public const string AcceptButtonSelector = ".cmpboxbtnyes";

        public static HtmlNode? GetAcceptAllButton(HtmlDocument doc)
        {
            var button = doc.DocumentNode
                .Descendants("a")
                .FirstOrDefault(x => x.HasClass("cmpboxbtnyes"));
            return button;
        }
    }
}
