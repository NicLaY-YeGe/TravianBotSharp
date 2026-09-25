using HtmlAgilityPack;
using System.Globalization;
using System.Text.RegularExpressions;

namespace MainCore.Parsers
{
    // Parses the SAME #tileDetails dialog OasisTileParser reads (opened by ToMapTileCommand:
    // Map page -> jump to X/Y -> click viewport), but for a DIFFERENT tile variant: a natural
    // "Abandoned valley" (k.vt) oasis shows a "Land distribution" table (Woodcutters/Clay
    // Pits/Iron Mines/Croplands counts) instead of the "Unoccupied oasis" animal-troop table
    // OasisTileParser handles. Real capture, 2026-09-24: (18|-1), 9 Croplands, 15.03 fields -
    // see MainCore.Test/Parsers/CropScan/TileDetailsAbandonedValley.html.
    //
    // Croplands is read directly from the table's own numeric cell (td.val next to i.r4) -
    // no need to decode it from the 'k.f1'..'k.f13' oasis-type name strings (e.g. "3-3-3-9")
    // also present in the page's translation table; the number is already right there.
    public static class CropOasisTileParser
    {
        private static readonly Regex CoordinateInt = new(@"-?\d+", RegexOptions.Compiled);
        private static readonly Regex DecimalNumber = new(@"\d+(\.\d+)?", RegexOptions.Compiled);

        public static bool IsAbandonedValley(HtmlDocument doc)
        {
            var tile = OasisTileParser.GetTileDetails(doc);
            if (tile is null) return false;

            var header = tile.Descendants("h1").FirstOrDefault(x => x.HasClass("titleInHeader"));
            if (header is null) return false;

            return OffensiveReportParser.NormalizeText(header.InnerText).Contains("Abandoned valley", StringComparison.OrdinalIgnoreCase);
        }

        // (X, Y) from the h1 header's own coordinate spans - same markup/bidi quirks as every
        // other coordinate on this game (see OffensiveReportParser.NormalizeText).
        public static (int X, int Y)? GetCoordinates(HtmlDocument doc)
        {
            var tile = OasisTileParser.GetTileDetails(doc);
            if (tile is null) return null;

            var header = tile.Descendants("h1").FirstOrDefault(x => x.HasClass("titleInHeader"));
            if (header is null) return null;

            var xNode = header.Descendants("span").FirstOrDefault(n => n.HasClass("coordinateX"));
            var yNode = header.Descendants("span").FirstOrDefault(n => n.HasClass("coordinateY"));
            if (xNode is null || yNode is null) return null;

            var xMatch = CoordinateInt.Match(OffensiveReportParser.NormalizeText(xNode.InnerText));
            var yMatch = CoordinateInt.Match(OffensiveReportParser.NormalizeText(yNode.InnerText));
            if (!xMatch.Success || !yMatch.Success) return null;

            if (!int.TryParse(xMatch.Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var x)) return null;
            if (!int.TryParse(yMatch.Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var y)) return null;

            return (x, y);
        }

        // table#distribution row whose icon is <i class="r4">, i.e. Croplands. Returns null if
        // the table/row isn't there at all (not an oasis dialog, or a future markup change).
        public static int? GetCroplands(HtmlDocument doc)
        {
            var tile = OasisTileParser.GetTileDetails(doc);
            if (tile is null) return null;

            var table = tile.Descendants("table").FirstOrDefault(x => x.Id == "distribution");
            if (table is null) return null;

            var tbody = table.Descendants("tbody").FirstOrDefault();
            if (tbody is null) return null;

            foreach (var row in tbody.Elements("tr"))
            {
                var icon = row.Descendants("i").FirstOrDefault(x => x.HasClass("r4"));
                if (icon is null) continue;

                var valCell = row.Descendants("td").FirstOrDefault(x => x.HasClass("val"));
                if (valCell is null) continue;

                if (int.TryParse(OffensiveReportParser.NormalizeText(valCell.InnerText), NumberStyles.None, CultureInfo.InvariantCulture, out var croplands))
                {
                    return croplands;
                }
            }

            return null;
        }

        // The second table's "Distance" row, e.g. "15.03 fields". Only used for the CSV
        // column/log line - never for filtering (that's done by CropScanCoordinateRules against
        // the coordinate the bot itself computed, which is authoritative).
        public static double? GetDistance(HtmlDocument doc)
        {
            var tile = OasisTileParser.GetTileDetails(doc);
            if (tile is null) return null;

            var descCell = tile.Descendants("td")
                .FirstOrDefault(x => x.HasClass("desc") && OffensiveReportParser.NormalizeText(x.InnerText).Equals("Distance", StringComparison.OrdinalIgnoreCase));
            if (descCell is null) return null;

            var valueCell = descCell.ParentNode?.Descendants("td").FirstOrDefault(x => x.HasClass("bold"));
            if (valueCell is null) return null;

            var match = DecimalNumber.Match(OffensiveReportParser.NormalizeText(valueCell.InnerText));
            if (!match.Success) return null;

            return double.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var distance) ? distance : null;
        }
    }
}
