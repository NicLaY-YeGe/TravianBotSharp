namespace MainCore.Parsers
{
    // Parses the "Send resources" tab of the Marketplace building (build.php?...&gid=17&t=5).
    // Verified against a real page capture (Turkish client, T4.6-style layout, July 2026).
    public static class SendResourceParser
    {
        public static HtmlNode? GetContainer(HtmlDocument doc)
        {
            return doc.GetElementbyId("marketplaceSendResources");
        }

        public static HtmlNode? GetForm(HtmlDocument doc)
        {
            var container = GetContainer(doc);
            return container?.Descendants("form").FirstOrDefault();
        }

        public static HtmlNode? GetXInput(HtmlDocument doc)
        {
            var form = GetForm(doc);
            return form?.Descendants("input").FirstOrDefault(x => x.GetAttributeValue("name", "") == "x");
        }

        public static HtmlNode? GetYInput(HtmlDocument doc)
        {
            var form = GetForm(doc);
            return form?.Descendants("input").FirstOrDefault(x => x.GetAttributeValue("name", "") == "y");
        }

        // resourceType: "lumber", "clay", "iron", "crop"
        public static HtmlNode? GetResourceInput(HtmlDocument doc, string resourceType)
        {
            var form = GetForm(doc);
            return form?.Descendants("input").FirstOrDefault(x => x.GetAttributeValue("name", "") == resourceType);
        }

        public static HtmlNode? GetSendButton(HtmlDocument doc)
        {
            var form = GetForm(doc);
            if (form is null) return null;

            return form.Descendants("button")
                .FirstOrDefault(x => x.GetAttributeValue("type", "") == "submit" && x.HasClass("send"));
        }

        public static bool IsSendButtonEnabled(HtmlDocument doc)
        {
            var button = GetSendButton(doc);
            if (button is null) return false;
            return !button.HasClass("disabled");
        }

        // Free merchants currently available in THIS village, out of the total merchant slots.
        // Shown near the top of the page as "Satici: <free>/<total>" (label text is localized).
        public static int GetFreeMerchants(HtmlDocument doc)
        {
            var node = doc.DocumentNode
                .Descendants("div")
                .FirstOrDefault(x => x.HasClass("merchantsInformation"))
                ?.Descendants("div")
                .FirstOrDefault(x => x.HasClass("available"))
                ?.Descendants("span")
                .FirstOrDefault(x => x.HasClass("value"));

            if (node is null) return 0;

            var parts = node.InnerText.Split('/');
            if (parts.Length != 2) return 0;

            var free = parts[0].ParseInt();
            return free < 0 ? 0 : free;
        }

        // How much resource a single merchant can carry.
        public static long GetMerchantCapacity(HtmlDocument doc)
        {
            var node = doc.DocumentNode
                .Descendants("div")
                .FirstOrDefault(x => x.HasClass("merchantCarryInfo"))
                ?.Descendants("strong")
                .FirstOrDefault();

            if (node is null) return 0;
            var value = node.InnerText.ParseLong();
            return value < 0 ? 0 : value;
        }
    }
}
