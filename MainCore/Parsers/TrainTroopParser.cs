namespace MainCore.Parsers
{
    public static class TrainTroopParser
    {
        public static HtmlNode? GetInputBox(HtmlDocument doc, TroopEnums troop)
        {
            var node = GetNode(doc, troop);
            if (node is null) return null;
            var cta = node.Descendants("div")
                .FirstOrDefault(x => x.HasClass("cta"));
            if (cta is null) return null;

            var input = cta.Descendants("input")
                .FirstOrDefault(x => x.HasClass("text"));
            return input;
        }

        public static int GetMaxAmount(HtmlDocument doc, TroopEnums troop)
        {
            var node = GetNode(doc, troop);
            if (node is null) return 0;
            var cta = node.Descendants("div")
                .FirstOrDefault(x => x.HasClass("cta"));
            if (cta is null) return 0;
            var a = cta.Descendants("a")
                .FirstOrDefault();
            if (a is null) return 0;

            return a.InnerText.ParseInt();
        }

        public static HtmlNode GetTrainButton(HtmlDocument doc)
        {
            return doc.GetElementbyId("s1");
        }

        // Present (already-trained-and-idle) count of a troop in THIS village, shown next to
        // its portrait on the Residence/Palace "Train" tab. Added 2026-08-12/13 for Auto
        // Settle (see CLAUDE.md/PROJECT_CONTEXT.md §5k). ASSUMPTION, not yet verified against
        // a real page capture - re-check the "value"/"animatedNumber" class names against a
        // live Residence/Palace Train tab if this ever returns 0 unexpectedly.
        public static int GetPresentAmount(HtmlDocument doc, TroopEnums troop)
        {
            var node = GetNode(doc, troop);
            if (node is null) return 0;

            var details = node.Descendants("div")
                .FirstOrDefault(x => x.HasClass("details"));
            if (details is null) return 0;

            var value = details.Descendants()
                .FirstOrDefault(x => x.HasClass("value") || x.HasClass("animatedNumber"));
            if (value is null) return 0;

            return value.InnerText.ParseInt();
        }

        // Per-unit resource cost (wood, clay, iron, crop), read from the same troop block's
        // cost display. Added alongside GetPresentAmount - same "not yet page-verified"
        // caveat applies.
        public static (long Wood, long Clay, long Iron, long Crop) GetUnitCost(HtmlDocument doc, TroopEnums troop)
        {
            var node = GetNode(doc, troop);
            if (node is null) return (0, 0, 0, 0);

            var wrapper = node.Descendants("div")
                .FirstOrDefault(x => x.HasClass("resourceWrapper"));
            if (wrapper is null) return (0, 0, 0, 0);

            var values = wrapper.Descendants("div")
                .Where(x => x.HasClass("resource"))
                .Select(x => x.InnerText.ParseLong())
                .ToList();

            if (values.Count < 4) return (0, 0, 0, 0);
            return (values[0], values[1], values[2], values[3]);
        }

        private static HtmlNode? GetNode(HtmlDocument doc, TroopEnums troop)
        {
            var nodes = doc.DocumentNode.Descendants("div")
               .Where(x => x.HasClass("troop"))
               .Where(x => !x.HasClass("empty"))
               .AsEnumerable();

            foreach (var node in nodes)
            {
                var img = node.Descendants("img")
                .FirstOrDefault(x => x.HasClass("unit"));
                if (img is null) continue;
                var classes = img.GetClasses();
                var type = classes
                    .Where(x => x.StartsWith('u'))
                    .FirstOrDefault(x => !x.Equals("unit"));
                if (type is null) continue;
                if (type.ParseInt() == (int)troop) return node;
            }
            return null;
        }
    }
}