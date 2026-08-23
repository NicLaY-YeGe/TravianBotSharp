using System.Text;
using System.Text.RegularExpressions;

namespace MainCore.Parsers
{
    // A village row from the server's public map.sql export. This file is not part of the
    // logged-in game session - it's a plain static SQL dump most Travian servers publish at
    // "<server>/map.sql" for third-party map tools, listing every occupied tile on the map.
    //
    // Column order verified 2026-08-21 against a real downloaded map.sql (Europe 11):
    //   INSERT INTO `x_world` VALUES (id, x, y, tid, vid, village, uid, player, aid, ally, pop, NULL, capital, NULL, NULL, NULL);
    // `tid` lines up exactly with this project's own TribeEnums numeric values (confirmed by
    // real data: every tid=5 row in the sample had village name "Natars X|Y" and player name
    // "Natars", matching TribeEnums.Natars = 5) - so tid can be cast to TribeEnums directly.
    public sealed record MapVillage(
        int Id,
        int X,
        int Y,
        int Tid,
        int VillageId,
        string VillageName,
        int PlayerId,
        string PlayerName,
        int AllianceId,
        string AllianceTag,
        int Population,
        bool IsCapital);

    public static class MapSqlParser
    {
        private static readonly Regex RowRegex = new(@"INSERT INTO `x_world` VALUES \((.+?)\);", RegexOptions.Compiled);

        public static List<MapVillage> Parse(string sqlContent)
        {
            var result = new List<MapVillage>();

            foreach (Match match in RowRegex.Matches(sqlContent))
            {
                var fields = SplitTopLevel(match.Groups[1].Value);
                // id,x,y,tid,vid,village,uid,player,aid,ally,pop,NULL,capital,NULL,NULL,NULL = 16 fields.
                // Only the first 13 are ever read below, but require the full shape so a
                // truncated/malformed line (unexpected export format) is skipped rather than
                // silently misread.
                if (fields.Count < 13) continue;

                if (!int.TryParse(fields[0], out var id)) continue;
                if (!int.TryParse(fields[1], out var x)) continue;
                if (!int.TryParse(fields[2], out var y)) continue;
                if (!int.TryParse(fields[3], out var tid)) continue;
                if (!int.TryParse(fields[4], out var villageId)) continue;
                var villageName = fields[5];
                if (!int.TryParse(fields[6], out var playerId)) continue;
                var playerName = fields[7];
                _ = int.TryParse(fields[8], out var allianceId);
                var allianceTag = fields[9];
                _ = int.TryParse(fields[10], out var population);
                var isCapital = fields[12].Trim().Equals("TRUE", StringComparison.OrdinalIgnoreCase);

                // Unoccupied tiles (oases, empty valleys) have no real owner - only real
                // villages matter for a "who's near this coordinate" search.
                if (playerId <= 0 || string.IsNullOrWhiteSpace(playerName)) continue;

                result.Add(new MapVillage(id, x, y, tid, villageId, villageName, playerId, playerName, allianceId, allianceTag, population, isCapital));
            }

            return result;
        }

        // Splits the comma-separated tuple content of one INSERT row into its raw field
        // strings, respecting single-quoted string literals (so commas/names inside a quoted
        // village or player name don't get treated as field separators). Handles both of the
        // two escaping conventions SQL dumps commonly use inside a quoted string: a doubled
        // quote ('') and a backslash-escaped character (\').
        private static List<string> SplitTopLevel(string content)
        {
            var fields = new List<string>();
            var sb = new StringBuilder();
            var inQuotes = false;

            for (var i = 0; i < content.Length; i++)
            {
                var c = content[i];

                if (inQuotes)
                {
                    if (c == '\\' && i + 1 < content.Length)
                    {
                        sb.Append(content[i + 1]);
                        i++;
                    }
                    else if (c == '\'')
                    {
                        if (i + 1 < content.Length && content[i + 1] == '\'')
                        {
                            sb.Append('\'');
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
                else
                {
                    if (c == '\'')
                    {
                        inQuotes = true;
                    }
                    else if (c == ',')
                    {
                        fields.Add(sb.ToString().Trim());
                        sb.Clear();
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
            }
            fields.Add(sb.ToString().Trim());

            return fields;
        }
    }
}
