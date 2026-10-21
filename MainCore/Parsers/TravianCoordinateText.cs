using System.Net;

namespace MainCore.Parsers
{
    // Travian prints coordinates with invisible bidi control characters around every number and
    // a real minus sign (U+2212) instead of '-', e.g. the live capture of 2026-10-20:
    //   Raid unoccupied oasis <LRO>(<LRO>35<PDF>|<LRO>U+2212<LRO>5<PDF><PDF>)<PDF>
    // so a plain Contains("(35|-5)") on the raw text NEVER matches. That is why Recall could
    // not find the abort button of a Dodge attack (CHANGELOG 2026-10-20). Everything that
    // compares coordinate text must normalise first. Pure (System only) so it is unit tested.
    public static class TravianCoordinateText
    {
        public static string Normalize(string? text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;

            var decoded = WebUtility.HtmlDecode(text);
            var sb = new System.Text.StringBuilder(decoded.Length);

            foreach (var c in decoded)
            {
                if (IsInvisible(c) || char.IsWhiteSpace(c)) continue;
                sb.Append(IsDash(c) ? '-' : c);
            }

            return sb.ToString();
        }

        // True when the (messy) text contains the coordinate "(x|y)".
        public static bool ContainsCoordinate(string? text, int x, int y)
            => Normalize(text).Contains($"({x}|{y})", StringComparison.Ordinal);

        private static bool IsInvisible(char c) =>
            (c >= '\u200B' && c <= '\u200F')   // zero-width, LRM, RLM
            || (c >= '\u202A' && c <= '\u202E') // LRE, RLE, PDF, LRO, RLO
            || (c >= '\u2066' && c <= '\u2069') // isolates
            || c == '\uFEFF';

        private static bool IsDash(char c) => c == '\u2212' || c == '\u2013' || c == '\u2014' || c == '\u2012';
    }
}
