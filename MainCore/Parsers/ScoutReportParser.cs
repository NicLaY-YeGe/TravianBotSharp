using System.Globalization;
using System.Text.RegularExpressions;

namespace MainCore.Parsers
{
    // One row of /report/scouting. Everything here comes from the LIST page alone:
    //   Outcome                - the result icon (class iReportNN). Seen on a real page
    //                            (2026-09-21): 15 = our spying was successful and not
    //                            detected, 18 = WE stopped somebody's spying, 19 = we could
    //                            NOT stop somebody's spying. The page's own quick-navigation
    //                            filter for OUR scouting is [15,16,17] (16 and 17 were not
    //                            seen yet - most likely "successful but detected" and
    //                            "failed", which is why only 15 is treated as certain by
    //                            callers that need the resources).
    //   IsOurScouting          - true for 15/16/17 (a scouting WE sent); false for 18/19
    //                            (somebody scouted US) - the subject text says the same
    //                            ("A_1201 scouts Volantis" vs "pejken city scouts A_1201")
    //                            but it is translated, the icon class is not
    //   DetailHref             - the subject link from "?id=..." on (relative to
    //                            /report/scouting), as rendered
    public sealed record ScoutReportRow(long ReportId, int Outcome, bool IsOurScouting, string DetailHref);

    public sealed record ScoutResources(int Lumber, int Clay, int Iron, int Crop)
    {
        public int Total => Lumber + Clay + Iron + Crop;
    }

    // An opened scouting report. Tile ids are the "d" of /karte.php?d=... links (see
    // MapTiles). Resources is null when the mission did not reveal them (e.g. a "defence and
    // troops" mission); CrannyCapacity is null when the report has no cranny line at all
    // (0 is a real value: no cranny).
    public sealed record ScoutReportDetail(
        int AttackerTileId,
        int DefenderTileId,
        ScoutResources? Resources,
        int? CrannyCapacity);

    // Parses /report/scouting (list) and an opened scouting report, both captured from a
    // real English-client account on 2026-09-21 (fixtures: MainCore.Test/Parsers/ScoutReport).
    // Language independent like OffensiveReportParser: only class names, numbers and links.
    public static class ScoutReportParser
    {
        // Building type id of the Cranny (the "typeNN" class of the report's building icon).
        private const string CrannyIconClass = "type23";

        private static readonly Regex OutcomeClass = new(@"(?:^|\s)iReport(\d+)(?:\s|$)", RegexOptions.Compiled);
        private static readonly Regex ReportIdInHref = new(@"[?&]id=(\d+)", RegexOptions.Compiled);
        private static readonly Regex TileIdInHref = new(@"[?&]d=(\d+)", RegexOptions.Compiled);
        private static readonly Regex NonDigits = new(@"[^\d]", RegexOptions.Compiled);

        public static bool IsScoutReportListPage(HtmlDocument doc)
        {
            var content = doc.GetElementbyId("content");
            return content is not null
                && content.HasClass("reportsScouting")
                && doc.GetElementbyId("overview") is not null;
        }

        public static bool IsScoutReportDetailPage(HtmlDocument doc)
        {
            var content = doc.GetElementbyId("content");
            return content is not null
                && content.HasClass("reportsScouting")
                && doc.GetElementbyId("reportWrapper") is not null;
        }

        public static bool IsOurScoutingOutcome(int outcome) => outcome is >= 15 and <= 17;

        public static IReadOnlyList<ScoutReportRow> GetReportRows(HtmlDocument doc)
        {
            var table = doc.GetElementbyId("overview");
            if (table is null) return Array.Empty<ScoutReportRow>();

            var tbody = table.Descendants("tbody").FirstOrDefault();
            if (tbody is null) return Array.Empty<ScoutReportRow>();

            var result = new List<ScoutReportRow>();
            foreach (var tr in tbody.Elements("tr"))
            {
                var row = ParseRow(tr);
                if (row is not null) result.Add(row);
            }
            return result;
        }

        private static ScoutReportRow? ParseRow(HtmlNode tr)
        {
            // The subject link is the only one carrying "id=<report id>%7C<hash>" - the
            // read/unread toggle link in the same row is just "#". A saved page has it
            // absolute, the live DOM relative ("?id=..."), so only the query part is used.
            var subjectLink = tr.Descendants("a").FirstOrDefault(x =>
                ReportIdInHref.IsMatch(System.Net.WebUtility.HtmlDecode(x.GetAttributeValue("href", ""))));
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

            var query = href.IndexOf('?');
            var detailHref = query >= 0 ? href[query..] : href;

            return new ScoutReportRow(reportId, outcome, IsOurScoutingOutcome(outcome), detailHref);
        }

        // Returns null when the page is not an opened report or has no attacker/defender block.
        public static ScoutReportDetail? ParseDetail(HtmlDocument doc)
        {
            var wrapper = doc.GetElementbyId("reportWrapper");
            if (wrapper is null) return null;

            var attacker = wrapper.Descendants("div").FirstOrDefault(x => x.HasClass("role") && x.HasClass("attacker"));
            var defender = wrapper.Descendants("div").FirstOrDefault(x => x.HasClass("role") && x.HasClass("defender"));
            if (attacker is null || defender is null) return null;

            var attackerTileId = GetVillageTileId(attacker);
            var defenderTileId = GetVillageTileId(defender);

            // The revealed information sits in the ATTACKER block (the scout's owner is the
            // one who learns it): table.additionalInformation with one lumber/clay/iron/crop
            // icon row and a second row with the cranny icon and the carry total.
            ScoutResources? resources = null;
            int? cranny = null;

            var info = attacker.Descendants("table").FirstOrDefault(x => x.HasClass("additionalInformation"));
            if (info is not null)
            {
                resources = GetResources(info);
                cranny = GetCrannyCapacity(info);
            }

            return new ScoutReportDetail(attackerTileId, defenderTileId, resources, cranny);
        }

        private static int GetVillageTileId(HtmlNode role)
        {
            var link = role.Descendants("a").FirstOrDefault(x => x.HasClass("village"));
            if (link is null) return 0;

            var href = System.Net.WebUtility.HtmlDecode(link.GetAttributeValue("href", ""));
            var match = TileIdInHref.Match(href);
            if (!match.Success) return 0;

            return int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var tileId) ? tileId : 0;
        }

        private static ScoutResources? GetResources(HtmlNode info)
        {
            int? lumber = ReadIconValue(info, "lumber");
            int? clay = ReadIconValue(info, "clay");
            int? iron = ReadIconValue(info, "iron");
            int? crop = ReadIconValue(info, "crop");

            if (lumber is null || clay is null || iron is null || crop is null) return null;

            return new ScoutResources(lumber.Value, clay.Value, iron.Value, crop.Value);
        }

        private static int? GetCrannyCapacity(HtmlNode info)
        {
            var icon = info.Descendants("i").FirstOrDefault(x => x.HasClass("building_small") && x.HasClass(CrannyIconClass));
            return icon is null ? null : ReadValueBesideIcon(icon);
        }

        // <div class="inlineIcon resources"><i class="lumber"></i><span class="value ">1638</span></div>
        private static int? ReadIconValue(HtmlNode info, string iconClass)
        {
            var icon = info.Descendants("i").FirstOrDefault(x => x.HasClass(iconClass));
            return icon is null ? null : ReadValueBesideIcon(icon);
        }

        private static int? ReadValueBesideIcon(HtmlNode icon)
        {
            var value = icon.ParentNode?.Descendants("span").FirstOrDefault(x => x.HasClass("value"));
            if (value is null) return null;

            // Thousands separators and invisible bidi marks are stripped by keeping digits only.
            var digits = NonDigits.Replace(OffensiveReportParser.NormalizeText(value.InnerText), "");
            return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : null;
        }
    }
}
