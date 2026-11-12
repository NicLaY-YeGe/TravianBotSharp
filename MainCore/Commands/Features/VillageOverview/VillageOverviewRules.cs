using System.Globalization;

namespace MainCore.Commands.Features.VillageOverview
{
    // 2026-11-11 (village overview tab): the text of every cell, as pure functions (no database, no browser) so the
    // wording and the edge cases are unit tested. `now` is always passed in.
    public static class VillageOverviewRules
    {
        public const string Unknown = "?";
        public const string None = "-";

        // "Phalanx 1,033, Swordsman 288" - only troops with a count above 0; "-" when there are none.
        public static string FormatTroops(string[] names, int[] counts)
        {
            var parts = new List<string>();
            for (var i = 0; i < counts.Length; i++)
            {
                if (counts[i] <= 0) continue;
                var name = i < names.Length && !string.IsNullOrWhiteSpace(names[i]) ? names[i] : $"Unit {i + 1}";
                parts.Add($"{name} {counts[i].ToString("N0", CultureInfo.CurrentCulture)}");
            }
            return parts.Count == 0 ? None : string.Join(", ", parts);
        }

        // Troops that are away from the village: on their way out + on their way back + stationed in other villages.
        public static int[] Outside(TroopSnapshot snapshot)
        {
            var length = snapshot.Home.Length;
            var result = new int[length];
            for (var i = 0; i < length; i++)
            {
                result[i] = At(snapshot.Moving, i) + At(snapshot.Returning, i) + At(snapshot.Stationed, i);
            }
            return result;
        }

        public static string HomeText(TroopSnapshot? snapshot)
            => snapshot is null ? Unknown : FormatTroops(snapshot.Names, snapshot.Home);

        public static string ReinforcementText(TroopSnapshot? snapshot)
            => snapshot is null ? Unknown : FormatTroops(snapshot.Names, snapshot.Reinforcement);

        // A snapshot that could not be read completely (the page shows only 10 movements and the "Show all" link did not
        // work) is marked with ">=": the real number is at least what is shown.
        public static string OutsideText(TroopSnapshot? snapshot)
        {
            if (snapshot is null) return Unknown;
            var text = FormatTroops(snapshot.Names, Outside(snapshot));
            return snapshot.Complete || text == None ? text : ">= " + text;
        }

        public static string FormatMerchants(MerchantSnapshot? snapshot)
            => snapshot is null ? Unknown : $"{snapshot.Free} home, {snapshot.Total - snapshot.Free} away (of {snapshot.Total})";

        // "idle" once the queue end has passed; otherwise "until 14:48 (202 min)".
        public static string FormatQueue(TrainQueueSnapshot? snapshot, DateTime now)
        {
            if (snapshot is null) return Unknown;
            if (snapshot.EndsAt <= now) return "idle";
            var minutes = (int)Math.Ceiling((snapshot.EndsAt - now).TotalMinutes);
            return $"until {snapshot.EndsAt:HH:mm} ({minutes} min)";
        }

        // Smithy / Small celebration: the village setting says whether the task is on and, while the building is busy,
        // until when (Unix seconds, 0 = none known).
        public static string FormatBusyUntil(bool enabled, long busyUntilUnixSeconds, DateTime now)
        {
            if (!enabled) return "off";
            if (busyUntilUnixSeconds <= 0) return "idle";
            var until = DateTimeOffset.FromUnixTimeSeconds(busyUntilUnixSeconds).LocalDateTime;
            if (until <= now) return "idle";
            var minutes = (int)Math.Ceiling((until - now).TotalMinutes);
            return $"busy until {until:HH:mm} ({minutes} min)";
        }

        // "8,843 (51%)" - the amount and how full the store is.
        public static string FormatStock(long amount, long capacity)
        {
            var text = amount.ToString("N0", CultureInfo.CurrentCulture);
            if (capacity <= 0) return text;
            var percent = (int)Math.Round(100.0 * amount / capacity);
            return $"{text} ({percent}%)";
        }

        public static string FormatCropBalance(long freeCrop)
            => (freeCrop >= 0 ? "+" : "") + freeCrop.ToString("N0", CultureInfo.CurrentCulture) + "/h";

        // The oldest of the read times of a row, as "5 min ago"; "never" when nothing was read.
        public static string FormatAgo(DateTime? readAt, DateTime now)
        {
            if (readAt is null) return "never";
            var span = now - readAt.Value;
            if (span < TimeSpan.Zero) span = TimeSpan.Zero;
            if (span.TotalSeconds < 60) return "just now";
            if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes} min ago";
            if (span.TotalHours < 48) return $"{(int)span.TotalHours} h ago";
            return $"{(int)span.TotalDays} d ago";
        }

        public static DateTime? Oldest(IEnumerable<DateTime?> times)
        {
            DateTime? oldest = null;
            foreach (var time in times)
            {
                if (time is null) continue;
                if (oldest is null || time.Value < oldest.Value) oldest = time;
            }
            return oldest;
        }

        private static int At(int[] values, int index) => index < values.Length ? values[index] : 0;
    }
}
