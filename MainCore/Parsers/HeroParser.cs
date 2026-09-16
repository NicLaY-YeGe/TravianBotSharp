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

        // 2026-09-15, added for OasisScoutTask's hero-safety check. The hero's actual combat
        // attack power lives in the "Fighting strength" attribute row (icon
        // "attributeStrength_medium"): a "name" div, then a "progressBar" div, then an
        // "inputRatio pointsRatio" div whose ".denominator .value" holds the real attack-power
        // number (2180 in a real capture) - the ".nominator" input next to it is just the raw
        // attribute POINTS invested, not the resulting combat stat, so that one is deliberately
        // NOT read here. Siblings run name -> progressBar -> inputRatio, repeated per
        // attribute row (strength, off bonus, def bonus, resources) under the same parent, so
        // walking forward from the strength icon's own "name" div to the first "inputRatio"
        // sibling lands on the strength row's value, not a later row's.
        public static int? GetAttackPower(HtmlDocument doc)
        {
            var strengthIcon = doc.DocumentNode
                .Descendants("i")
                .FirstOrDefault(x => x.HasClass("attributeStrength_medium"));
            if (strengthIcon is null) return null;

            var nameDiv = strengthIcon.ParentNode;
            if (nameDiv is null) return null;

            var sibling = nameDiv.NextSibling;
            while (sibling is not null && !(sibling.NodeType == HtmlNodeType.Element && sibling.HasClass("inputRatio")))
            {
                sibling = sibling.NextSibling;
            }
            if (sibling is null) return null;

            var valueDiv = sibling.Descendants("div").FirstOrDefault(x => x.HasClass("value"));
            if (valueDiv is null) return null;

            var match = Regex.Match(valueDiv.InnerText, @"\d+");
            if (!match.Success) return null;

            return int.Parse(match.Value);
        }
    }
}
