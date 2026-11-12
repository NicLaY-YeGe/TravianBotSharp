namespace MainCore.Parsers
{
    // 2026-11-11 (village overview tab): the troop numbers of the Rally Point "Overview" tab, split by where the troops
    // are. Language independent - it goes by the table classes and ids, never by the headings' text.
    //
    // CHECKED against two real captures (TS-50 Gaul account with stationed reinforcements, TS-12 account with 20 movements):
    //   - every block is a `table.troop_details`; the troops of a block are the `td.unit` cells of `tbody.units.last`
    //     (10 troops + hero = 11 cells, a zero cell carries the extra class `none`); the unit names are the `alt` of the
    //     `img.unit` icons of the first row;
    //   - `inReturn` = troops coming back, `out*` (outRaid, ...) = troops on their way out, any other `in*` (inAttack,
    //     inRaid, reinforcement coming in) is NOT ours to count;
    //   - a plain `troop_details` with a `tr#unitRowAtTown` row is "in this village": `data-did == data-ort` = the village's
    //     own troops, a different `data-did` with the same `data-player-name` = reinforcement of the player's other village;
    //   - a plain `troop_details` WITHOUT that row = our troops stationed in other villages/oases ("Reinforcement for X");
    //   - the page shows only 10 movements per section: the heading says "Incoming troops (12)" but only 10 tables follow
    //     and a "Show all" link (`a.openedClosedSwitch`, `?gid=16&tt=1&filter=1` incoming / `filter=2` outgoing) is there.
    //     IncomingTruncated / OutgoingTruncated report that, so the caller can follow the link instead of under-counting.
    // NOT checked: a foreign player's reinforcement in the village (ignored: another player's table is not ours), the
    // class of an outgoing attack/reinforcement (assumed `out*` like `outRaid`).
    public sealed record RallyPointTroops(
        string[] Names,
        int[] Home,
        int[] Reinforcement,
        int[] Moving,
        int[] Returning,
        int[] Stationed,
        bool IncomingTruncated,
        bool OutgoingTruncated,
        bool HasOwnTroopsTable);

    public static class RallyPointTroopsParser
    {
        public const int SlotCount = 11;

        private sealed class Group
        {
            public int Claimed = -1;
            public List<HtmlNode> Tables = new();
        }

        public static RallyPointTroops? Get(HtmlDocument doc)
        {
            var container = doc.DocumentNode
                .Descendants("div")
                .FirstOrDefault(x => x.HasClass("rallyPointOverviewContainer"));
            if (container is null) return null;

            var groups = new List<Group>();
            Group? current = null;
            foreach (var node in container.Descendants())
            {
                if (node.Name == "h4" && node.HasClass("spacer"))
                {
                    current = new Group { Claimed = ParseClaimedCount(node.InnerText) };
                    groups.Add(current);
                }
                else if (node.Name == "table" && node.HasClass("troop_details"))
                {
                    if (current is null)
                    {
                        current = new Group();
                        groups.Add(current);
                    }
                    current.Tables.Add(node);
                }
            }

            var home = new int[SlotCount];
            var reinforcement = new int[SlotCount];
            var moving = new int[SlotCount];
            var returning = new int[SlotCount];
            var stationed = new int[SlotCount];
            string[]? names = null;
            var incomingTruncated = false;
            var outgoingTruncated = false;

            var allTables = groups.SelectMany(x => x.Tables).ToList();
            var ownTable = allTables.FirstOrDefault(IsOwnVillageTable);
            var ownPlayer = ownTable?.GetAttributeValue("data-player-name", "");

            foreach (var group in groups)
            {
                if (group.Tables.Count == 0) continue;

                var truncated = group.Claimed > group.Tables.Count;
                if (truncated)
                {
                    if (group.Tables.All(IsIncoming)) incomingTruncated = true;
                    else if (group.Tables.All(IsOutgoing)) outgoingTruncated = true;
                }

                foreach (var table in group.Tables)
                {
                    names ??= GetNames(table);

                    if (HasClass(table, "inReturn"))
                    {
                        Add(returning, GetCounts(table));
                    }
                    else if (IsOutgoing(table))
                    {
                        Add(moving, GetCounts(table));
                    }
                    else if (IsIncoming(table))
                    {
                        // hostile movements and reinforcements coming in are not our troops
                    }
                    else if (HasHomeRow(table))
                    {
                        if (IsOwnVillageTable(table))
                        {
                            Add(home, GetCounts(table));
                        }
                        else if (ownPlayer is not null && ownPlayer.Length > 0
                            && table.GetAttributeValue("data-player-name", "") == ownPlayer)
                        {
                            Add(reinforcement, GetCounts(table));
                        }
                    }
                    else
                    {
                        Add(stationed, GetCounts(table));
                    }
                }
            }

            return new RallyPointTroops(
                names ?? DefaultNames(),
                home, reinforcement, moving, returning, stationed,
                incomingTruncated, outgoingTruncated, ownTable is not null);
        }

        private static bool HasClass(HtmlNode table, string name)
        {
            return table.GetAttributeValue("class", "")
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Contains(name);
        }

        private static bool IsIncoming(HtmlNode table)
        {
            return table.GetAttributeValue("class", "")
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Any(x => x.StartsWith("in", StringComparison.Ordinal));
        }

        private static bool IsOutgoing(HtmlNode table)
        {
            return table.GetAttributeValue("class", "")
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Any(x => x.StartsWith("out", StringComparison.Ordinal));
        }

        private static bool HasHomeRow(HtmlNode table)
        {
            return table.Descendants("tr").Any(x => x.Id == "unitRowAtTown");
        }

        // data-did = the village the troops belong to, data-ort = the village they stand in; equal = the village's own troops.
        private static bool IsOwnVillageTable(HtmlNode table)
        {
            if (!HasHomeRow(table)) return false;
            var did = table.GetAttributeValue("data-did", "");
            var ort = table.GetAttributeValue("data-ort", "");
            if (did.Length == 0 || ort.Length == 0) return true;
            return did == ort;
        }

        private static int ParseClaimedCount(string heading)
        {
            var text = System.Net.WebUtility.HtmlDecode(heading ?? "");
            var open = text.LastIndexOf('(');
            var close = text.LastIndexOf(')');
            if (open < 0 || close <= open) return -1;
            return text.Substring(open + 1, close - open - 1).ParseInt();
        }

        private static int[] GetCounts(HtmlNode table)
        {
            var counts = new int[SlotCount];
            var row = table.Descendants("tbody")
                .FirstOrDefault(x => x.HasClass("units") && x.HasClass("last"));
            if (row is null) return counts;

            var cells = row.Descendants("td").Where(x => x.HasClass("unit")).ToList();
            for (var i = 0; i < SlotCount && i < cells.Count; i++)
            {
                var value = cells[i].InnerText.ParseInt();
                counts[i] = value < 0 ? 0 : value;
            }
            return counts;
        }

        private static string[]? GetNames(HtmlNode table)
        {
            var alts = table.Descendants("img")
                .Where(x => x.HasClass("unit"))
                .Select(x => x.GetAttributeValue("alt", ""))
                .Take(SlotCount)
                .ToList();
            if (alts.Count == 0) return null;

            var names = DefaultNames();
            for (var i = 0; i < alts.Count; i++)
            {
                if (alts[i].Length > 0) names[i] = alts[i];
            }
            return names;
        }

        private static string[] DefaultNames()
        {
            var names = new string[SlotCount];
            for (var i = 0; i < SlotCount - 1; i++) names[i] = $"Unit {i + 1}";
            names[SlotCount - 1] = "Hero";
            return names;
        }

        private static void Add(int[] total, int[] add)
        {
            for (var i = 0; i < SlotCount; i++) total[i] += add[i];
        }
    }
}
