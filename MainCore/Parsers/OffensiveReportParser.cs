using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MainCore.Parsers
{
    // One row of /report/offensive. Everything here comes from the LIST page alone (no report
    // has to be opened for it):
    //   Outcome        - the result icon: 1 = won without losses, 2 = won with losses,
    //                    3 = lost as attacker (class iReport1/2/3; the mapping comes from the
    //                    page's own filter buttons, so it does not depend on the UI language)
    //   HasLootInfo    - false when the row has no carry icon at all (a hero-only oasis raid
    //                    has capacity 0, and for those the icon is simply not rendered)
    //   LootCarried/LootCapacity - "42/225" from the carry icon
    //   HasCoordinates - the subject only contains coordinates for oases and for villages whose
    //                    NAME contains them (Natars); for ordinary player villages it is just
    //                    the village name and the report has to be opened to find out where it
    //                    went (see OffensiveReportDetail)
    //   DetailHref     - the subject link exactly as rendered ("?id=19345928|75e76c3b&s=1"),
    //                    relative to /report/offensive
    public sealed record OffensiveReportRow(
        long ReportId,
        int Outcome,
        bool HasLootInfo,
        int LootCarried,
        int LootCapacity,
        bool HasCoordinates,
        int TargetX,
        int TargetY,
        string DetailHref);

    // An opened offensive report. Tile ids are the "d" of /karte.php?d=... links - they encode
    // a map position (see MapTiles). LootCarried/LootCapacity are null when the report has no
    // bounty line.
    public sealed record OffensiveReportDetail(
        int AttackerTileId,
        int DefenderTileId,
        bool HasDefenderCoordinates,
        int DefenderX,
        int DefenderY,
        long TroopsSent,
        long TroopsDead,
        int? LootCarried,
        int? LootCapacity);

    // Parses /report/offensive (list) and the opened report pages behind it, both captured
    // from a real English-client account on 2026-09-19 (see the fixtures under
    // MainCore.Test/Parsers/OffensiveReport). Deliberately language independent: only class
    // names, numbers and links are used, never the (translated) texts.
    public static class OffensiveReportParser
    {
        private static readonly Regex OutcomeClass = new(@"(?:^|\s)iReport(\d+)(?:\s|$)", RegexOptions.Compiled);
        private static readonly Regex ReportIdInHref = new(@"[?&]id=(\d+)", RegexOptions.Compiled);
        private static readonly Regex TileIdInHref = new(@"[?&]d=(\d+)", RegexOptions.Compiled);
        private static readonly Regex Integer = new(@"-?\d+", RegexOptions.Compiled);
        private static readonly Regex Fraction = new(@"(\d+)/(\d+)", RegexOptions.Compiled);
        private static readonly Regex CoordinatesInName = new(@"(?<![\d-])(-?\d{1,4})\s*\|\s*(-?\d{1,4})(?!\d)", RegexOptions.Compiled);

        // The list page has table#overview; an opened report has #reportWrapper - and BOTH
        // have #content.reportsOffensive, so the content class alone cannot tell them apart.
        public static bool IsOffensiveReportListPage(HtmlDocument doc)
        {
            var content = doc.GetElementbyId("content");
            return content is not null
                && content.HasClass("reportsOffensive")
                && doc.GetElementbyId("overview") is not null;
        }

        public static bool IsOffensiveReportDetailPage(HtmlDocument doc)
        {
            var content = doc.GetElementbyId("content");
            return content is not null
                && content.HasClass("reportsOffensive")
                && doc.GetElementbyId("reportWrapper") is not null;
        }

        public static bool HasNextPage(HtmlDocument doc)
        {
            return doc.DocumentNode.Descendants("div")
                .Where(x => x.HasClass("paginator"))
                .Any(x => x.Descendants("a").Any(a => a.HasClass("next")));
        }

        public static IReadOnlyList<OffensiveReportRow> GetReportRows(HtmlDocument doc)
        {
            var table = doc.GetElementbyId("overview");
            if (table is null) return Array.Empty<OffensiveReportRow>();

            var tbody = table.Descendants("tbody").FirstOrDefault();
            if (tbody is null) return Array.Empty<OffensiveReportRow>();

            var result = new List<OffensiveReportRow>();
            foreach (var tr in tbody.Elements("tr"))
            {
                var row = ParseRow(tr);
                if (row is not null) result.Add(row);
            }
            return result;
        }

        private static OffensiveReportRow? ParseRow(HtmlNode tr)
        {
            var subjectLink = tr.Descendants("a")
                .FirstOrDefault(x => x.GetAttributeValue("href", "").StartsWith("?id=", StringComparison.Ordinal));
            if (subjectLink is null) return null;

            var href = System.Net.WebUtility.HtmlDecode(subjectLink.GetAttributeValue("href", ""));

            long reportId = 0;
            var checkbox = tr.Descendants("input").FirstOrDefault(x => x.HasClass("report"));
            if (checkbox is not null)
            {
                long.TryParse(checkbox.GetAttributeValue("value", ""), NumberStyles.None, CultureInfo.InvariantCulture, out reportId);
            }
            if (reportId <= 0)
            {
                var idMatch = ReportIdInHref.Match(href);
                if (!idMatch.Success || !long.TryParse(idMatch.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out reportId)) return null;
            }

            var outcome = 0;
            foreach (var img in tr.Descendants("img"))
            {
                var match = OutcomeClass.Match(img.GetAttributeValue("class", ""));
                if (!match.Success) continue;
                int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out outcome);
                break;
            }

            var hasLootInfo = false;
            var lootCarried = 0;
            var lootCapacity = 0;
            var carryImg = tr.Descendants("img").FirstOrDefault(x => x.HasClass("reportInfo") && x.HasClass("carry"));
            if (carryImg is not null)
            {
                // The game strips the title attribute from the live DOM (tooltip system) but
                // leaves alt - which Selenium's PageSource therefore always has.
                var text = carryImg.GetAttributeValue("alt", "");
                if (string.IsNullOrWhiteSpace(text)) text = carryImg.GetAttributeValue("title", "");
                hasLootInfo = TryParseFraction(text, out lootCarried, out lootCapacity);
            }

            var hasCoordinates = TryGetCoordinates(subjectLink, out var targetX, out var targetY);
            if (!hasCoordinates)
            {
                var nameMatch = CoordinatesInName.Match(NormalizeText(subjectLink.InnerText));
                if (nameMatch.Success
                    && int.TryParse(nameMatch.Groups[1].Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out targetX)
                    && int.TryParse(nameMatch.Groups[2].Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out targetY))
                {
                    hasCoordinates = true;
                }
            }

            return new OffensiveReportRow(reportId, outcome, hasLootInfo, lootCarried, lootCapacity, hasCoordinates, targetX, targetY, href);
        }

        // Returns null when the page is not an opened report or has no attacker/defender block.
        public static OffensiveReportDetail? ParseDetail(HtmlDocument doc)
        {
            var wrapper = doc.GetElementbyId("reportWrapper");
            if (wrapper is null) return null;

            var attacker = wrapper.Descendants("div").FirstOrDefault(x => x.HasClass("role") && x.HasClass("attacker"));
            var defender = wrapper.Descendants("div").FirstOrDefault(x => x.HasClass("role") && x.HasClass("defender"));
            if (attacker is null || defender is null) return null;

            var attackerTileId = GetVillageTileId(attacker, out _);
            var defenderTileId = GetVillageTileId(defender, out var defenderLink);

            var hasDefenderCoordinates = false;
            int defenderX = 0, defenderY = 0;
            if (defenderLink is not null)
            {
                hasDefenderCoordinates = TryGetCoordinates(defenderLink, out defenderX, out defenderY);
            }

            GetTroopTotals(attacker, out var sent, out var dead);

            int? lootCarried = null;
            int? lootCapacity = null;
            var carry = attacker.Descendants("div").FirstOrDefault(x => x.HasClass("inlineIcon") && x.HasClass("carry"));
            var carryValue = carry?.Descendants("span").FirstOrDefault(x => x.HasClass("value"));
            if (carryValue is not null && TryParseFraction(carryValue.InnerText, out var carried, out var capacity))
            {
                lootCarried = carried;
                lootCapacity = capacity;
            }

            return new OffensiveReportDetail(
                attackerTileId, defenderTileId, hasDefenderCoordinates, defenderX, defenderY,
                sent, dead, lootCarried, lootCapacity);
        }

        // "a.village" inside the role block's headline: /karte.php?d=<tile id>. 0 when absent.
        private static int GetVillageTileId(HtmlNode role, out HtmlNode? link)
        {
            link = role.Descendants("a").FirstOrDefault(x => x.HasClass("village"));
            if (link is null) return 0;

            var href = System.Net.WebUtility.HtmlDecode(link.GetAttributeValue("href", ""));
            var match = TileIdInHref.Match(href);
            if (!match.Success) return 0;

            return int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var tileId) ? tileId : 0;
        }

        // The role block has an icon row, a "troopCount" row (sent) and, when anything was
        // lost, a "troopDead" row. Cells can be "?" (hidden information, e.g. the defender's
        // troops) - those count as 0; only the attacker's rows are summed here anyway.
        private static void GetTroopTotals(HtmlNode role, out long sent, out long dead)
        {
            sent = 0;
            dead = 0;

            foreach (var tbody in role.Descendants("tbody").Where(x => x.HasClass("units")))
            {
                var tr = tbody.Element("tr");
                if (tr is null) continue;

                var icon = tr.Element("th")?.Descendants("i").FirstOrDefault();
                var iconClass = icon?.GetAttributeValue("class", "") ?? "";
                var isCount = iconClass.Contains("troopCount", StringComparison.Ordinal);
                var isDead = iconClass.Contains("troopDead", StringComparison.Ordinal);
                if (!isCount && !isDead) continue;

                long total = 0;
                foreach (var td in tr.Elements("td").Where(x => x.HasClass("unit")))
                {
                    if (long.TryParse(NormalizeText(td.InnerText).Replace(",", ""), NumberStyles.None, CultureInfo.InvariantCulture, out var value))
                    {
                        total += value;
                    }
                }

                if (isCount) sent += total;
                else dead += total;
            }
        }

        // <span class="coordinateX">(152</span><span class="coordinatePipe">|</span><span
        // class="coordinateY">-74)</span> - both numbers wrapped in invisible direction marks,
        // and the minus sign is a real U+2212, not a hyphen (see NormalizeText).
        private static bool TryGetCoordinates(HtmlNode container, out int x, out int y)
        {
            x = 0;
            y = 0;

            var xNode = container.Descendants("span").FirstOrDefault(n => n.HasClass("coordinateX"));
            var yNode = container.Descendants("span").FirstOrDefault(n => n.HasClass("coordinateY"));
            if (xNode is null || yNode is null) return false;

            var xMatch = Integer.Match(NormalizeText(xNode.InnerText));
            var yMatch = Integer.Match(NormalizeText(yNode.InnerText));
            if (!xMatch.Success || !yMatch.Success) return false;

            return int.TryParse(xMatch.Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out x)
                && int.TryParse(yMatch.Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out y);
        }

        // "42/225" -> (42, 225). Thousands separators (",", ".", spaces) are dropped first.
        public static bool TryParseFraction(string? text, out int carried, out int capacity)
        {
            carried = 0;
            capacity = 0;

            var cleaned = Regex.Replace(NormalizeText(text), @"[,.\s\u202F]", "");
            var match = Fraction.Match(cleaned);
            if (!match.Success) return false;

            return int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out carried)
                && int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out capacity);
        }

        // Travian wraps numbers in invisible bidi-control characters (U+202D/U+202C, U+200E,
        // ...) and renders a negative number's sign as U+2212 (&minus;). Both break a plain
        // int.Parse - the same trap as the "[40|-2]" coordinates elsewhere in this project - so
        // every text read from these pages goes through here first. HTML entities are decoded
        // first because InnerText leaves them as-is when the source came from view-source
        // instead of the live DOM.
        public static string NormalizeText(string? text)
        {
            if (string.IsNullOrEmpty(text)) return "";

            var decoded = System.Net.WebUtility.HtmlDecode(text);
            var builder = new StringBuilder(decoded.Length);
            foreach (var ch in decoded)
            {
                if (ch is '\u200E' or '\u200F' or (>= '\u202A' and <= '\u202E') or (>= '\u2066' and <= '\u2069')) continue;

                builder.Append(ch switch
                {
                    '\u2212' => '-',
                    '\u00A0' => ' ',
                    _ => ch,
                });
            }
            return builder.ToString().Trim();
        }
    }

    // Travian encodes a map position as a "tile id": d = 1 + (x + R) + (R - y) * (2R + 1),
    // where R is the map radius (400 -> 801x801 tiles, 200 -> 401x401). R is not exposed
    // anywhere on the pages this bot reads, but every village of OURS is known by both its
    // coordinates (Villages table) and - from any report it appears in - its tile id, so R is
    // inferred from that pair (InferRadius) instead of being configured. Verified against real
    // captures from a radius-200 server: (144|-82) = d 113427, (140|-84) = 114225,
    // (152|-74) = 110227, (150|-81) = 113032.
    public static class MapTiles
    {
        // Larger than any real Travian map; only bounds the search in InferRadius.
        private const int MaxRadius = 1000;

        public static int ToTileId(int x, int y, int radius)
        {
            return 1 + (x + radius) + (radius - y) * (2 * radius + 1);
        }

        public static (int X, int Y)? ToCoordinates(int tileId, int radius)
        {
            var width = 2 * radius + 1;
            var n = tileId - 1;
            if (n < 0 || n >= width * width) return null;

            return (n % width - radius, radius - n / width);
        }

        // The map radius for which (x|y) has exactly this tile id, or null if none does.
        public static int? InferRadius(int tileId, int x, int y)
        {
            for (var radius = Math.Max(Math.Abs(x), Math.Abs(y)); radius <= MaxRadius; radius++)
            {
                if (ToTileId(x, y, radius) == tileId) return radius;
            }
            return null;
        }
    }
}
