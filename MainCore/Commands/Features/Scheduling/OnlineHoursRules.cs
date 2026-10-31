namespace MainCore.Commands.Features.Scheduling
{
    // 2026-10-30: the account's online hours are a 24-bit mask (bit N = hour N, AccountSettingEnums.OnlineHoursMask).
    // Until now only "is the CURRENT hour online" was asked (SettingService.IsCurrentHourOnline). The training
    // queue horizon (QueueHorizonRules) needs to look AHEAD: when does the next offline stretch start and when
    // does the bot work again, so a queue can be filled to last through the gap. Pure, unit tested.
    //
    // Hours are whole clock hours of the local time, exactly like IsCurrentHourOnline. The scan looks at most
    // 48 hours ahead; a mask with no offline hour (all online) or no online hour (never online) returns null.
    public static class OnlineHoursRules
    {
        public const int AllHours = (1 << 24) - 1;
        private const int ScanHours = 48;

        public static bool IsOnline(int mask, int hour)
        {
            if (hour < 0 || hour > 23) return false;
            return (mask & (1 << hour)) != 0;
        }

        // The moment the bot stops working: `now` itself when the current hour is already offline, otherwise the
        // start of the first offline hour after now. Null when every hour is online (no gap to plan for).
        public static DateTime? NextOfflineStart(int mask, DateTime now)
        {
            if ((mask & AllHours) == AllHours) return null;
            if (!IsOnline(mask, now.Hour)) return now;

            var hourStart = StartOfHour(now);
            for (var i = 1; i <= ScanHours; i++)
            {
                var boundary = hourStart.AddHours(i);
                if (!IsOnline(mask, boundary.Hour)) return boundary;
            }

            return null;
        }

        // The moment the bot works again: `from` itself when its hour is online, otherwise the start of the first
        // online hour after it. Null when no hour is online at all.
        public static DateTime? NextOnlineStart(int mask, DateTime from)
        {
            if ((mask & AllHours) == 0) return null;
            if (IsOnline(mask, from.Hour)) return from;

            var hourStart = StartOfHour(from);
            for (var i = 1; i <= ScanHours; i++)
            {
                var boundary = hourStart.AddHours(i);
                if (IsOnline(mask, boundary.Hour)) return boundary;
            }

            return null;
        }

        private static DateTime StartOfHour(DateTime time)
        {
            return new DateTime(time.Year, time.Month, time.Day, time.Hour, 0, 0, time.Kind);
        }
    }
}
