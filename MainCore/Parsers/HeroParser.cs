using System.Text.RegularExpressions;

namespace MainCore.Parsers
{
    public static class HeroParser
    {
        public static bool IsAttributesPage(HtmlDocument doc)
        {
            var content = doc.GetElementbyId("content");
            if (content is null) return false;
            return content.HasClass("heroV2Attributes");
        }

        // Health is shown as a "Health" stat block (icon "attributeHealth_medium") containing
        // a progress bar whose filled portion is a <div class="filling ..." style="width: X%;">.
        // The clean, robust percentage lives here - the top bar hero widget shown on every page
        // encodes the same info as an SVG arc, which is far more fragile to parse.
        public static int? GetHealthPercent(HtmlDocument doc)
        {
            var healthIcon = doc.DocumentNode
                .Descendants("i")
                .FirstOrDefault(x => x.HasClass("attributeHealth_medium"));
            if (healthIcon is null) return null;

            // <i> -> "name" div -> "stats" div (sibling "progressBar" div holds the bar)
            var statsBox = healthIcon.ParentNode?.ParentNode;
            if (statsBox is null) return null;

            var filling = statsBox.Descendants("div")
                .FirstOrDefault(x => x.HasClass("filling"));
            if (filling is null) return null;

            var style = filling.GetAttributeValue("style", "");
            var match = Regex.Match(style, @"width:\s*(\d+)%");
            if (!match.Success) return null;

            return int.Parse(match.Groups[1].Value);
        }
    }
}
