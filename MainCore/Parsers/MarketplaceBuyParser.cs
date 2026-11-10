using System.Text.RegularExpressions;

namespace MainCore.Parsers
{
    // 2026-11-10: parser for the Marketplace "Buy" tab. Written against two real captures (TS12, Marketplace level 10,
    // before/after accepting an offer, and an earlier level-8 capture) - see CHANGELOG 2026-11-10.
    //   - container: <div id="marketplaceBuy">; merchants: .merchantsInformation (.available "6/10", .capacity "975")
    //   - filters: <div class="filterWrapper"> = label + filterGroup (searching) + label + filterGroup (ratio) + ...
    //     the first group's button[data-option=4] is crop, the second group's only button is "1:1 or better";
    //     an active filter button carries the class "active"
    //   - rows: table.offers > tbody > tr with td.offered / td.wanted (<i class="crop_small"> + <span>), td.merchants,
    //     td.duration, td.player, td.accept > button (class "disabled" when it cannot be accepted)
    //   - pagination: div.pagination with button.back / button.page(.selected) / button.forward
    // Nothing here depends on the client language.
    public static class MarketplaceBuyParser
    {
        public static HtmlNode? GetContainer(HtmlDocument doc) => doc.GetElementbyId("marketplaceBuy");

        public static bool HasOffersTable(HtmlDocument doc) => GetTable(doc) is not null;

        private static HtmlNode? GetTable(HtmlDocument doc)
        {
            var container = GetContainer(doc);
            return container?.Descendants("table").FirstOrDefault(x => x.HasClass("offers"));
        }

        // The tab bar entry of the Buy tab: href ends with "t=1" (position-independent: a server without the gold
        // "Trade routes" tab has the tabs at other indexes). Null when the page has no such tab.
        public static HtmlNode? GetBuyTab(HtmlDocument doc)
        {
            var bar = BuildingTabParser.GetNavigationBar(doc);
            if (bar is null) return null;
            return bar.Descendants("a")
                .Where(x => x.HasClass("tabItem"))
                .FirstOrDefault(x =>
                {
                    var href = System.Net.WebUtility.HtmlDecode(x.GetAttributeValue("href", ""));
                    return Regex.IsMatch(href, @"[?&]t=1$");
                });
        }

        // "6/10" -> 6. Null when the line is not on the page (yet).
        public static int? GetFreeMerchants(HtmlDocument doc)
        {
            var text = GetMerchantValue(doc, "available");
            if (text is null) return null;
            var parts = text.Split('/');
            var free = parts[0].ParseInt();
            return free < 0 ? null : free;
        }

        public static int? GetMerchantCapacity(HtmlDocument doc)
        {
            var text = GetMerchantValue(doc, "capacity");
            if (text is null) return null;
            var value = text.ParseInt();
            return value < 0 ? null : value;
        }

        private static string? GetMerchantValue(HtmlDocument doc, string cssClass)
        {
            var container = GetContainer(doc);
            var info = container?.Descendants("div").FirstOrDefault(x => x.HasClass("merchantsInformation"));
            var block = info?.Descendants("div").FirstOrDefault(x => x.HasClass(cssClass));
            var value = block?.Descendants("span").FirstOrDefault(x => x.HasClass("value"));
            return value?.InnerText;
        }

        public static List<HtmlNode> GetFilterGroups(HtmlDocument doc)
        {
            var container = GetContainer(doc);
            var wrapper = container?.Descendants("div").FirstOrDefault(x => x.HasClass("filterWrapper"));
            if (wrapper is null) return [];
            return wrapper.ChildNodes.Where(x => x.Name == "div" && x.HasClass("filterGroup")).ToList();
        }

        // Filter group 0 = "I'm searching" (data-option 1..4 = wood, clay, iron, crop).
        public static bool? IsCropSearchActive(HtmlDocument doc)
        {
            var groups = GetFilterGroups(doc);
            if (groups.Count < 1) return null;
            var crop = groups[0].Descendants("button").FirstOrDefault(x => x.GetAttributeValue("data-option", "") == "4");
            if (crop is null) return null;
            return crop.HasClass("active");
        }

        // Filter group 1 = "Ratio" (a single button, "1:1 or better").
        public static bool? IsRatioFilterActive(HtmlDocument doc)
        {
            var groups = GetFilterGroups(doc);
            if (groups.Count < 2) return null;
            var button = groups[1].Descendants("button").FirstOrDefault();
            if (button is null) return null;
            return button.HasClass("active");
        }

        // True when only crop is searched, no other resource filter is active.
        public static bool? IsOnlyCropSearched(HtmlDocument doc)
        {
            var groups = GetFilterGroups(doc);
            if (groups.Count < 1) return null;
            var buttons = groups[0].Descendants("button").ToList();
            if (buttons.Count == 0) return null;
            foreach (var button in buttons)
            {
                var isCrop = button.GetAttributeValue("data-option", "") == "4";
                if (isCrop != button.HasClass("active")) return false;
            }
            return true;
        }

        public static IReadOnlyList<MarketplaceOffer> GetOffers(HtmlDocument doc)
        {
            var table = GetTable(doc);
            if (table is null) return [];
            var body = table.Descendants("tbody").FirstOrDefault();
            if (body is null) return [];

            var offers = new List<MarketplaceOffer>();
            var index = 0;
            foreach (var row in body.ChildNodes.Where(x => x.Name == "tr"))
            {
                index++;
                var offer = ParseRow(row, index);
                if (offer is not null) offers.Add(offer);
            }
            return offers;
        }

        private static MarketplaceOffer? ParseRow(HtmlNode row, int index)
        {
            var offeredCell = GetCell(row, "offered");
            var wantedCell = GetCell(row, "wanted");
            var merchantCell = GetCell(row, "merchants");
            var durationCell = GetCell(row, "duration");
            if (offeredCell is null || wantedCell is null || merchantCell is null || durationCell is null) return null;

            var offeredType = GetResourceType(offeredCell);
            var wantedType = GetResourceType(wantedCell);
            if (offeredType is null || wantedType is null) return null;

            var offeredAmount = GetAmount(offeredCell);
            var wantedAmount = GetAmount(wantedCell);
            if (offeredAmount <= 0 || wantedAmount <= 0) return null;

            var merchantSpan = merchantCell.Descendants("span").FirstOrDefault();
            var merchants = (merchantSpan?.InnerText ?? merchantCell.InnerText).ParseInt();
            if (merchants < 0) return null;

            if (!TryParseDuration(durationCell.InnerText, out var duration)) return null;

            var wantedSpan = wantedCell.Descendants("span").FirstOrDefault();
            var resourceMissing = wantedSpan is not null && wantedSpan.HasClass("notEnough");
            var merchantsMissing = merchantSpan is not null && merchantSpan.HasClass("notEnough");

            var button = GetCell(row, "accept")?.Descendants("button").FirstOrDefault();
            var enabled = button is not null
                && !button.HasClass("disabled")
                && !button.Attributes.Contains("disabled");

            var player = GetCell(row, "player")?.InnerText.Trim() ?? "";

            return new MarketplaceOffer(index, offeredType, offeredAmount, wantedType, wantedAmount, merchants, duration, enabled, resourceMissing, merchantsMissing, player);
        }

        private static HtmlNode? GetCell(HtmlNode row, string cssClass)
        {
            return row.ChildNodes.FirstOrDefault(x => x.Name == "td" && x.HasClass(cssClass));
        }

        // <i class="crop_small"> / lumber_small / clay_small / iron_small
        private static string? GetResourceType(HtmlNode cell)
        {
            var icon = cell.Descendants("i").FirstOrDefault();
            if (icon is null) return null;
            var classes = icon.GetClasses().ToList();
            if (classes.Contains("lumber_small")) return "wood";
            if (classes.Contains("clay_small")) return "clay";
            if (classes.Contains("iron_small")) return "iron";
            if (classes.Contains("crop_small")) return "crop";
            return null;
        }

        private static long GetAmount(HtmlNode cell)
        {
            var span = cell.Descendants("span").FirstOrDefault();
            return (span?.InnerText ?? "").ParseLong();
        }

        private static bool TryParseDuration(string text, out TimeSpan duration)
        {
            duration = default;
            // ParseInt in ToDuration drops the invisible direction marks the game puts around numbers
            var cleaned = text.Trim();
            if (cleaned.Count(c => c == ':') != 2) return false;
            try
            {
                duration = cleaned.ToDuration();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // (selected page, page count); null when the page has no pagination (a single page of offers).
        public static (int Selected, int Count)? GetPagination(HtmlDocument doc)
        {
            var container = GetContainer(doc);
            var pagination = container?.Descendants("div").FirstOrDefault(x => x.HasClass("pagination"));
            if (pagination is null) return null;
            var pages = pagination.Descendants("button").Where(x => x.HasClass("page")).ToList();
            if (pages.Count == 0) return null;
            var selected = pages.FindIndex(x => x.HasClass("selected")) + 1;
            return (selected, pages.Count);
        }

        public static bool IsForwardEnabled(HtmlDocument doc)
        {
            var container = GetContainer(doc);
            var pagination = container?.Descendants("div").FirstOrDefault(x => x.HasClass("pagination"));
            var forward = pagination?.Descendants("button").FirstOrDefault(x => x.HasClass("forward"));
            return forward is not null && !forward.HasClass("disabled");
        }
    }
}
