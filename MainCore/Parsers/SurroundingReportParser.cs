using System.Globalization;
using System.Text.RegularExpressions;

namespace MainCore.Parsers
{
    // Parses /report/surrounding - a list of historical "An oasis has been plundered." events
    // near the currently active village (there's no village id in the URL; distance is always
    // relative to whichever village is active when the page loads - confirmed against a real
    // capture, 2026-09-15). Each oasis typically appears many times (once per past plunder
    // event), so callers should use GetUniqueOasisCoordinates rather than the raw row list.
    public static class SurroundingReportParser
    {
        public static bool IsSurroundingReportPage(HtmlDocument doc)
        {
            var content = doc.GetElementbyId("content");
            return content is not null && content.HasClass("reportsSurrounding");
        }

        // 2026-11-25: the coordinate link of a row is /map?x=-31&y=28 on the newer servers (live capture
        // T-5X, Parsers/SurroundingReport/Surrounding_NewScheme.html) and /karte.php?x=..&y=.. on the old
        // ones. The parser used to look for "karte.php" only, so every row was skipped silently and Oasis
        // scout reported "no oasis reports" for days. Now any link with x=..&y=.. counts (links with ?d=
        // carry no coordinates and are ignored), and only rows that are oasis events are returned: the page
        // also lists "A fight took place in <village>" rows with their own coordinates.
        private static readonly Regex CoordinateRegex = new(@"[?&]x=(-?\d+)&y=(-?\d+)", RegexOptions.Compiled);

        public static IReadOnlyList<HtmlNode> GetRows(HtmlDocument doc)
        {
            var table = doc.GetElementbyId("overview");
            var tbody = table?.Descendants("tbody").FirstOrDefault();
            if (tbody is null) return Array.Empty<HtmlNode>();
            return tbody.Elements("tr").ToList();
        }

        // The icon class is language independent; the text is only a fallback for a row without an icon.
        public static bool IsOasisRow(HtmlNode row)
        {
            var icon = row.Descendants("div").FirstOrDefault(x => x.HasClass("reportIcon"));
            if (icon is not null) return icon.HasClass("oasisOccupy");

            var text = row.Descendants("div").FirstOrDefault(x => x.HasClass("reportText"))?.InnerText ?? "";
            return text.TrimStart().StartsWith("An oasis", StringComparison.OrdinalIgnoreCase);
        }

        public static IReadOnlyList<(int X, int Y, double Distance)> GetOasisReportRows(HtmlDocument doc)
        {
            var result = new List<(int, int, double)>();

            foreach (var row in GetRows(doc))
            {
                if (!IsOasisRow(row)) continue;

                var cell = row.Descendants("td").FirstOrDefault(x => x.HasClass("coords"));
                var links = (cell ?? row).Descendants("a");

                var match = Match.Empty;
                foreach (var link in links)
                {
                    // GetAttributeValue already HTML-decodes entities in normal use, but this is
                    // decoded again defensively in case a page variant leaves "&amp;" raw.
                    var href = System.Net.WebUtility.HtmlDecode(link.GetAttributeValue("href", ""));
                    match = CoordinateRegex.Match(href);
                    if (match.Success) break;
                }

                if (!match.Success) continue;

                var x = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                var y = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);

                var distCell = row.Descendants("td").FirstOrDefault(c => c.HasClass("dist"));
                if (distCell is null) continue;

                var distText = distCell.InnerText.Trim().Replace(',', '.');
                if (!double.TryParse(distText, NumberStyles.Float, CultureInfo.InvariantCulture, out var distance)) continue;

                result.Add((x, y, distance));
            }

            return result;
        }

        // Dedupes by (X,Y) - keeping the smallest reported distance for that tile as a cheap
        // safety net against any rounding differences across rows - filters to maxDistance,
        // and sorts nearest-first, matching the user's explicit "start from the closest
        // coordinate" requirement.
        public static IReadOnlyList<(int X, int Y, double Distance)> GetUniqueOasisCoordinates(HtmlDocument doc, double maxDistance)
        {
            return GetOasisReportRows(doc)
                .GroupBy(r => (r.X, r.Y))
                .Select(g => g.OrderBy(r => r.Distance).First())
                .Where(r => r.Distance <= maxDistance)
                .OrderBy(r => r.Distance)
                .ToList();
        }
    }
}
