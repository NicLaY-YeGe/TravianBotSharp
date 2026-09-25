using HtmlAgilityPack;

using HtmlAgilityPack;

using HtmlAgilityPack;

using HtmlAgilityPack;

namespace MainCore.Parsers
{
    // Parses the tile-detail dialog opened by ToMapTileCommand (Map page -> jump to X/Y ->
    // click the map viewport). Same dialog structure confirmed against two real oasis
    // captures (2026-09-15) - one with animals present, one empty.
    public static class OasisTileParser
    {
        public static HtmlNode? GetTileDetails(HtmlDocument doc) => doc.GetElementbyId("tileDetails");

        public static bool IsTileDialogOpen(HtmlDocument doc) => GetTileDetails(doc) is not null;

        // Only an "Unoccupied oasis" is a wild-animal oasis safe to raid/hunt freely - anything
        // else (occupied by a player, a regular village tile, or something unexpected) is
        // deliberately treated as off-limits by the caller (OasisScoutTask.cs), since acting on
        // a player-held tile is a completely different, much higher-stakes action than what
        // this feature is for.
        public static bool IsUnoccupiedOasis(HtmlDocument doc)
        {
            var tile = GetTileDetails(doc);
            if (tile is null) return false;

            var header = tile.Descendants("h1").FirstOrDefault(x => x.HasClass("titleInHeader"));
            if (header is null) return false;

            return header.InnerText.Contains("Unoccupied oasis", StringComparison.OrdinalIgnoreCase);
        }

        // Returns (display name, count) pairs read from the "Troops" table. An empty list
        // means either a genuinely empty oasis ("none") or an unrecognized/empty row (e.g.
        // "No information available!" if never scouted) - both are treated as "no animals" by
        // the caller, which is the safe direction for this specific check.
        //
        // A second table sharing the SAME id="troop_info" exists elsewhere in this same
        // dialog (the "Reports" tab, class="rep transparent") - a real Travian markup quirk,
        // not a bug here. Excluded explicitly via the "rep" class rather than relying on DOM
        // order, since relying on "first match wins" would silently break if that order ever
        // changes on a future page variant.
        public static IReadOnlyList<(string Name, int Count)> GetAnimals(HtmlDocument doc)
        {
            var tile = GetTileDetails(doc);
            if (tile is null) return Array.Empty<(string, int)>();

            var table = tile.Descendants("table")
                .FirstOrDefault(x => x.Id == "troop_info" && !x.HasClass("rep"));
            if (table is null) return Array.Empty<(string, int)>();

            var tbody = table.Descendants("tbody").FirstOrDefault();
            if (tbody is null) return Array.Empty<(string, int)>();

            var result = new List<(string, int)>();
            foreach (var row in tbody.Elements("tr"))
            {
                var nameCell = row.Descendants("td").FirstOrDefault(x => x.HasClass("desc"));
                var countCell = row.Descendants("td").FirstOrDefault(x => x.HasClass("val"));
                if (nameCell is null || countCell is null) continue;

                var name = nameCell.InnerText.Trim();
                if (!int.TryParse(countCell.InnerText.Trim(), out var count) || count <= 0) continue;

                result.Add((name, count));
            }

            return result;
        }
    }
}
