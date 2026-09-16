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

        public static IReadOnlyList<(int X, int Y, double Distance)> GetOasisReportRows(HtmlDocument doc)
        {
            var table = doc.GetElementbyId("overview");
            if (table is null) return Array.Empty<(int, int, double)>();

            var tbody = table.Descendants("tbody").FirstOrDefault();
            if (tbody is null) return Array.Empty<(int, int, double)>();

            var result = new List<(int, int, double)>();

            foreach (var row in tbody.Elements("tr"))
            {
                var coordLink = row.Descendants("a")
                    .FirstOrDefault(x => x.GetAttributeValue("href", "").Contains("karte.php"));
                if (coordLink is null) continue;

                // GetAttributeValue already HTML-decodes entities in normal use, but this is
                // decoded again defensively in case a future page variant leaves "&amp;" raw -
                // cheap and harmless either way.
                var href = System.Net.WebUtility.HtmlDecode(coordLink.GetAttributeValue("href", ""));
                var match = Regex.Match(href, @"x=(-?\d+)&y=(-?\d+)");
                if (!match.Success) continue;

                var x = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                var y = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);

                var distCell = row.Descendants("td").FirstOrDefault(x => x.HasClass("dist"));
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
