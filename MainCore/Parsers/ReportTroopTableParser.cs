using System.Text.RegularExpressions;

namespace MainCore.Parsers
{
    // Shared by OffensiveReportParser and ScoutReportParser: a report's attacker/defender "role"
    // block renders its troop composition with IDENTICAL markup in both report types - a header
    // row of <td class="uniticon"><img class="unit uNN" alt="Name"></td> (NN = the game's global
    // troop id - TroopEnums' underlying int values were deliberately laid out to match it, see
    // SmithyParser's comment) followed by one or more value rows of <td class="unit"> cells at
    // the same column index (identified by the row's <th><i class="..._small"> icon, e.g.
    // "troopCount_small"/"troopDead_small"). A hidden ("?") cell - the normal state for an
    // offensive report's DEFENDER row - is reported as null; a real number (0 included) is its
    // parsed value.
    //
    // Verified against real captures for the header row and a hidden ("?") value row
    // (MainCore.Test/Parsers/OffensiveReport/OffensiveReportDetail_Lost.html, 2026-09-19) and for
    // an all-zero REVEALED value row (MainCore.Test/Parsers/ScoutReport/
    // ScoutReportDetail_Success.html, 2026-09-21). A report that reveals a NONZERO defender troop
    // count - the actual point of a successful "spy defence/troops" scouting mission - has not
    // been captured yet; this is applied there on the strength of the shared markup alone. Flag
    // it (and ideally share a real capture) if the first live troops-mission report parses
    // unexpectedly.
    public static class ReportTroopTableParser
    {
        private static readonly Regex UnitIdClass = new(@"(?:^|\s)u(\d+)(?:\s|$)", RegexOptions.Compiled);

        // Column order (left to right) as TroopEnums, from the role block's header row.
        // TroopEnums.None marks a column this can't identify (the "uhero" hero column, or an
        // unrecognized class). Empty if the block/header row is missing.
        public static IReadOnlyList<TroopEnums> GetColumns(HtmlNode roleBlock)
        {
            var header = roleBlock.Descendants("tbody")
                .Where(x => x.HasClass("units"))
                .Select(x => x.Element("tr"))
                .FirstOrDefault(tr => tr is not null && tr.Elements("td").Any(td => td.HasClass("uniticon")));
            if (header is null) return Array.Empty<TroopEnums>();

            var columns = new List<TroopEnums>();
            foreach (var td in header.Elements("td").Where(x => x.HasClass("uniticon")))
            {
                var img = td.Descendants("img").FirstOrDefault(x => x.HasClass("unit"));
                var cls = img?.GetAttributeValue("class", "") ?? "";
                var match = UnitIdClass.Match(cls);
                columns.Add(match.Success && int.TryParse(match.Groups[1].Value, out var id)
                    ? (TroopEnums)id
                    : TroopEnums.None);
            }
            return columns;
        }

        // One value row (e.g. the "troopCount" row): null per cell = hidden ("?"), otherwise the
        // parsed count. rowIconClass is the class on the row's <th><i> that identifies it.
        // Empty if the block or that row is missing.
        public static IReadOnlyList<int?> GetRow(HtmlNode roleBlock, string rowIconClass)
        {
            var row = roleBlock.Descendants("tbody")
                .Where(x => x.HasClass("units"))
                .Select(x => x.Element("tr"))
                .FirstOrDefault(tr => tr?.Element("th")?.Descendants("i").Any(i => i.HasClass(rowIconClass)) == true);
            if (row is null) return Array.Empty<int?>();

            var values = new List<int?>();
            foreach (var td in row.Elements("td").Where(x => x.HasClass("unit")))
            {
                var text = OffensiveReportParser.NormalizeText(td.InnerText).Replace(",", "");
                values.Add(int.TryParse(text, out var count) ? count : (int?)null);
            }
            return values;
        }

        // Pairs GetColumns with one value row into a troop composition, dropping the hero/
        // unrecognized column and any zero-count column. Null when the row is hidden ("?") -
        // either fully, or (defensively) when it MIXES real numbers and "?", since a partial
        // reveal is not a game state this project has ever seen, so trusting only the numeric
        // cells would be guessing. An all-revealed, all-zero row returns an empty (non-null)
        // list - a real, useful "nothing there" result, distinct from "not revealed at all".
        public static IReadOnlyList<(TroopEnums Troop, int Count)>? GetComposition(HtmlNode roleBlock, string rowIconClass)
        {
            var columns = GetColumns(roleBlock);
            var values = GetRow(roleBlock, rowIconClass);
            if (columns.Count == 0 || values.Count == 0) return null;

            var result = new List<(TroopEnums, int)>();
            var sawHidden = false;
            var sawRevealed = false;

            for (var i = 0; i < Math.Min(columns.Count, values.Count); i++)
            {
                if (columns[i] == TroopEnums.None) continue;

                if (values[i] is null) { sawHidden = true; continue; }

                sawRevealed = true;
                if (values[i]!.Value > 0) result.Add((columns[i], values[i]!.Value));
            }

            if (sawHidden) return null;
            return sawRevealed ? result : null;
        }
    }
}
