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
            var form = container?.Descendants("form").FirstOrDefault();
            if (form is not null) return form;

            // Fallback: some server/client variants may not use the same container id.
            // Look for any form that has both an "x" and a "y" coordinate input - that's
            // specific enough to be the send-resources form and nothing else.
            return doc.DocumentNode
                .Descendants("form")
                .FirstOrDefault(f =>
                    f.Descendants("input").Any(i => i.GetAttributeValue("name", "") == "x")
                    && f.Descendants("input").Any(i => i.GetAttributeValue("name", "") == "y"));
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

        // resourceType: "wood", "clay", "iron", "crop" (our own naming). Travian's actual
        // HTML uses "lumber" for wood's input name specifically - everything else matches.
        public static HtmlNode? GetResourceInput(HtmlDocument doc, string resourceType)
        {
            var form = GetForm(doc);
            var htmlName = resourceType == "wood" ? "lumber" : resourceType;
            return form?.Descendants("input").FirstOrDefault(x => x.GetAttributeValue("name", "") == htmlName);
        }

        // The "+" button next to a resource's slider. Clicking it once adds exactly one
        // merchant's worth of that resource - this is the reliable way to fill in amounts,
        // since it goes through the page's own JS instead of us typing into the input.
        // Anchored on the resource's <input name="..."> (already proven reliable elsewhere)
        // rather than the icon's CSS class, which turned out to be less trustworthy.
        public static HtmlNode? GetPlusButton(HtmlDocument doc, string resourceType)
        {
            var input = GetResourceInput(doc, resourceType);
            if (input is null) return null;

            var container = input.Ancestors("div")
                .FirstOrDefault(x => x.GetAttributeValue("class", "") == "resourceSelector");
            if (container is null) return null;

            return container.Descendants("button").FirstOrDefault(x => x.HasClass("plus"));
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

            if (button.HasClass("disabled")) return false;
            if (button.GetAttributeValue("disabled", "__none__") != "__none__") return false;
            if (button.GetAttributeValue("aria-disabled", "") == "true") return false;

            return true;
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
