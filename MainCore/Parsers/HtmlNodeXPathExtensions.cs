namespace MainCore.Parsers
{
    // 2026-10-21: HtmlAgilityPack's HtmlNode.XPath is absolute and purely positional
    // ("/html[1]/body[1]/div[3]/div[3]/div[2]/div[1]/div[1]/div[21]"). Any element inserted or
    // removed in front of the target at any level (cookie banner, ad, extension-injected div, a
    // re-render) shifts an index, and the Playwright locator built from it then matches nothing
    // ("waiting for Locator(xpath=/html[1]/body[1]/div[3]...)", live log 13:00:59, TS12).
    //
    // StableXPath() anchors the path at the nearest ancestor (or the node itself) that has a
    // unique id, e.g. "//*[@id='villageContent']/div[21]", so only the part below that id is
    // positional. When no usable id exists it returns the plain absolute XPath, i.e. exactly what
    // was used before.
    public static class HtmlNodeXPathExtensions
    {
        public static string StableXPath(this HtmlNode node)
        {
            var full = node.XPath;

            for (var current = node; current is not null && current.NodeType == HtmlNodeType.Element; current = current.ParentNode)
            {
                var id = current.GetAttributeValue("id", "");
                if (string.IsNullOrWhiteSpace(id)) continue;
                if (id.Contains('\'') || id.Contains('"')) continue; // cannot be quoted safely
                if (!IsUnique(current, id)) continue;

                var anchor = current.XPath;
                if (!full.StartsWith(anchor, StringComparison.Ordinal)) continue;

                var rest = full[anchor.Length..];
                if (rest.Length > 0 && rest[0] != '/') continue; // "div[1]" vs "div[10]" boundary

                return $"//*[@id='{id}']{rest}";
            }

            return full;
        }

        private static bool IsUnique(HtmlNode node, string id)
        {
            var root = node.OwnerDocument?.DocumentNode;
            if (root is null) return false;

            var count = 0;
            foreach (var element in root.Descendants())
            {
                if (element.NodeType != HtmlNodeType.Element) continue;
                if (element.GetAttributeValue("id", "") != id) continue;
                if (++count > 1) return false;
            }

            return count == 1;
        }
    }
}
