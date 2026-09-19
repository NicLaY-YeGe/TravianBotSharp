using System.Text.RegularExpressions;

namespace MainCore.Parsers
{
    public static class HeroParser
    {
        // 2026-09-18: a live HTML capture (shared while diagnosing a 180s CheckHeroHealthCommand
        // timeout) showed the #content div using class "hero_inventoryAttributes" instead of
        // "heroV2Attributes" - the ONLY class this check recognized. That single-class check
        // meant IsAttributesPage() returned false forever on that page, even though the page was
        // genuinely, visibly on the Attributes tab - explaining the full-length timeout despite
        // nothing actually being wrong with the page. Root trigger (a client version rollout, an
        // alternate navigation route, or something else) isn't confirmed, so both known class
        // names are accepted rather than replacing one guess with another.
        private static readonly string[] AttributesPageContentClasses = ["heroV2Attributes", "hero_inventoryAttributes"];

        public static bool IsAttributesPage(HtmlDocument doc)
        {
            var content = doc.GetElementbyId("content");
            if (content is null) return false;
            return AttributesPageContentClasses.Any(content.HasClass);
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

        // The Attributes page shows this specific box when the hero has died - see
        // CLAUDE.md/PROJECT_CONTEXT.md for the live HTML sample this was built from
        // (2026-09-12). Works whether the page was reached via ToHeroAttributesPageCommand's
        // direct /hero/attributes navigation or by clicking the hero avatar from elsewhere -
        // a dead hero lands here either way.
        public static bool IsHeroDead(HtmlDocument doc)
        {
            return doc.DocumentNode.Descendants("div")
                .Any(x => x.HasClass("attributeBox") && x.HasClass("heroDead"));
        }

        // Any one of the four "revive with resources" icons - clicking any of them opens the
        // same shared resourceTransferDialog for all four resources at once (same pattern as
        // the Inventory tab's item grid - see UseHeroItemCommand.ClickItem /
        // InventoryParser.GetResourceTransferDialog, which the resulting dialog is read with).
        public static HtmlNode? GetReviveResourceIcon(HtmlDocument doc)
        {
            return doc.DocumentNode.Descendants("div")
                .FirstOrDefault(x => x.HasClass("inlineIcon") && x.HasClass("resource") && x.HasClass("transfer")
                    && x.GetAttributeValue("onclick", "").Contains("targetResourceAmount"));
        }

        // The exact resource amounts the game says are needed to auto-revive the hero, straight
        // out of the revive icon's inline onclick JS call
        // (window.Travian.React.Hero.openResourceTransfer({targetResourceAmount: {lumber: N,
        // clay: N, iron: N, crop: N}, ...})). This isn't rendered as plain text anywhere else on
        // the page - HtmlAgilityPack doesn't execute JS, so the onclick attribute string is
        // regexed directly rather than read as a live JS object. Returns null if the icon or any
        // of the four values can't be found (caller should treat that as "can't determine
        // revival need right now", not "nothing needed").
        public static Dictionary<string, long>? GetReviveTargetAmounts(HtmlDocument doc)
        {
            var icon = GetReviveResourceIcon(doc);
            var onclick = icon?.GetAttributeValue("onclick", "") ?? "";
            if (string.IsNullOrEmpty(onclick)) return null;

            var result = new Dictionary<string, long>();
            foreach (var name in new[] { "lumber", "clay", "iron", "crop" })
            {
                var match = Regex.Match(onclick, name + @"\s*:\s*(\d+)");
                if (!match.Success) return null;
                result[name] = long.Parse(match.Groups[1].Value);
            }
            return result;
        }

        // The hero's home village - where it will actually revive - straight from the
        // "Hero will be revived in village <a href=".../karte.php?d=113427">A_1201</a>" link.
        // `d=` here is the same internal game village id VillagePanelParser reads as
        // `data-did` (== this project's Village.Id/VillageId), NOT the village's X|Y
        // coordinates - unverified against a live account with the hero away from its own
        // capital, since the only sample available had home == the account's only village.
        public static int? GetHomeVillageGameId(HtmlDocument doc)
        {
            var link = doc.DocumentNode.Descendants("a")
                .FirstOrDefault(x => x.GetAttributeValue("href", "").Contains("karte.php?d="));
            var href = link?.GetAttributeValue("href", "") ?? "";
            var match = Regex.Match(href, @"d=(\d+)");
            return match.Success ? int.Parse(match.Groups[1].Value) : null;
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
