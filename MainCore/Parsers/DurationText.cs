namespace MainCore.Parsers
{
    // 2026-10-30: "H:MM:SS" clock text of the game (a troop's training time per unit, e.g. "0:05:47") to seconds.
    // Direction marks and other non-digit characters around the digits are ignored. 0 = unreadable.
    // Pure, unit tested.
    public static class DurationText
    {
        public static int ParseClockSeconds(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;

            var chars = new List<char>();
            foreach (var c in text)
            {
                if (char.IsDigit(c) || c == ':') chars.Add(c);
            }

            var parts = new string(chars.ToArray()).Split(':', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3) return 0;
            if (!int.TryParse(parts[0], out var hours)) return 0;
            if (!int.TryParse(parts[1], out var minutes)) return 0;
            if (!int.TryParse(parts[2], out var seconds)) return 0;
            if (hours < 0 || minutes < 0 || seconds < 0) return 0;

            return hours * 3600 + minutes * 60 + seconds;
        }
    }
}
