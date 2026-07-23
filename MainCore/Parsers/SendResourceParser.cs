using HtmlAgilityPack;
using System.Linq;

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

        // resourceType: "wood", "clay", "iron", "crop"
        public static HtmlNode? GetResourceInput(HtmlDocument doc, string resourceType)
        {
            var form = GetForm(doc);
            
            // Travian HTML form yapısındaki gerçek input name karşılıkları (r1, r2, r3, r4) ile eşleştirildi
            var inputName = resourceType switch
            {
                "wood" => "r1",
                "clay" => "r2",
                "iron" => "r3",
                "crop" => "r4",
                _ => resourceType
            };

            return form?.Descendants("input").FirstOrDefault(x => x.GetAttributeValue("name", "") == inputName);
        }

        // The "+" button next to a resource's slider. Clicking it once adds exactly one
        // merchant's worth of that resource - this is the reliable way to fill in amounts,
        // since it goes through the page's own JS instead of us typing into the input.
        public static HtmlNode? GetPlusButton(HtmlDocument doc, string resourceType)
        {
            var iconClass = resourceType switch
            {
                "wood" => "lumber_medium",
                "clay" => "clay_medium",
                "iron" => "iron_medium",
                "crop" => "crop_medium",
                _ => null,
            };
            if (iconClass is null) return null;

            // Doğru kaynağın ikonunu nokta atışı buluyoruz
            var iconNode = doc.DocumentNode.Descendants("i")
                .FirstOrDefault(i => i.GetAttributeValue("class", "").Contains(iconClass));

            if (iconNode is null) return null;

            // İkonun container hiyerarşisinde yukarı çıkarak sadece o kaynağa ait olan satırdaki plus butonunu seçiyoruz
            var currentNode = iconNode.ParentNode;
            while (currentNode != null && currentNode.Name != "form")
            {
                var button = currentNode.Descendants("button").FirstOrDefault(x => x.HasClass("plus"));
                if (button != null) return button;
                
                currentNode = currentNode.ParentNode;
            }

            return null;
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