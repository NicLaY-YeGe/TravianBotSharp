using System.Globalization;
using System.Text;

namespace MainCore.Parsers
{
    // 2026-10-31, user request: export the Map Analysis result as a CSV for a spreadsheet.
    // One row per village; the coordinate is ONE column written as "x|y" (e.g. "-12|8"), like in the game.
    // Separator is ';' and the distance uses the Turkish decimal comma, because Turkish Excel splits CSV
    // on ';' and reads "12,34" as a number (a '.' would become text or a date). Pure (no file, no UI), unit tested.
    public static class MapScanCsv
    {
        public const char Separator = ';';

        public static readonly CultureInfo DefaultCulture = new("tr-TR");

        public static string Header =>
            string.Join(Separator, "Mesafe", "Koordinat", "Köy", "Oyuncu", "Kabile", "İttifak", "Nüfus", "Başkent");

        // The coordinate exactly as the game shows it: "-12|8".
        public static string Coordinate(int x, int y) => $"{x}|{y}";

        public static string TribeText(int tid)
        {
            return Enum.IsDefined(typeof(TribeEnums), tid) ? ((TribeEnums)tid).ToString() : $"Tribe {tid}";
        }

        // `rows` are already sorted (nearest first); the order is kept.
        public static string Build(IEnumerable<(double Distance, MapVillage Village)> rows, CultureInfo? culture = null)
        {
            culture ??= DefaultCulture;
            var sb = new StringBuilder();
            sb.Append(Header).Append("\r\n");

            foreach (var (distance, v) in rows)
            {
                sb.Append(string.Join(Separator,
                    distance.ToString("0.00", culture),
                    Coordinate(v.X, v.Y),
                    Text(v.VillageName),
                    Text(v.PlayerName),
                    TribeText(v.Tid),
                    Text(v.AllianceTag),
                    v.Population.ToString(CultureInfo.InvariantCulture),
                    v.IsCapital ? "Evet" : "Hayır"));
                sb.Append("\r\n");
            }

            return sb.ToString();
        }

        // A name field typed by another player. Two protections:
        //  - a leading = + - @ (or tab / CR) would be run as a formula by Excel, so it is prefixed with an apostrophe;
        //  - a field holding the separator, a quote or a line break is quoted ("" for a quote inside).
        // The coordinate column is generated here (digits, '-' and '|' only) and is NOT passed through this.
        public static string Text(string? value)
        {
            if (string.IsNullOrEmpty(value)) return "";

            var text = value;
            if (text[0] is '=' or '+' or '-' or '@' or '\t' or '\r') text = "'" + text;

            if (text.IndexOfAny([Separator, '"', '\r', '\n']) >= 0)
            {
                text = "\"" + text.Replace("\"", "\"\"") + "\"";
            }

            return text;
        }
    }
}
