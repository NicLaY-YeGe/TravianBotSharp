namespace MainCore.Parsers
{
    // Parses the "Demolish building" section on the Main Building page
    // (build.php?...&gid=15) - it's on the same page, no separate tab.
    // Verified against a real page capture (English client, July 2026).
    public static class DemolishParser
    {
        public static HtmlNode? GetSelect(HtmlDocument doc)
        {
            return doc.GetElementbyId("demolish");
        }

        // location: the building's slot/location number, exactly as shown in the dropdown's
        // own option values (19 Town Hall 1, 26 Main Building 10, etc.) - matches
        // Building.Location already tracked in the DB, no name matching needed.
        public static bool HasOption(HtmlDocument doc, int location)
        {
            var select = GetSelect(doc);
            if (select is null) return false;

            return select.Descendants("option")
                .Any(o => o.GetAttributeValue("value", "") == $"{location}");
        }

        public static HtmlNode? GetDemolishButton(HtmlDocument doc)
        {
            return doc.GetElementbyId("btn_demolish");
        }
    }
}
