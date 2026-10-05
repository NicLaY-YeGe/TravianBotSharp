namespace MainCore.Commands.Features.SyncAttack
{
    // How long before the first send the bot should wake up, and how long after the last send
    // it may go back to sleep/offline - each a random pick in [Min, Max] minutes (2026-10-03,
    // user request: "çıkış saatinden önce uyan, gönder, uyu", 5-10 min each side by default).
    public sealed record WakeWindowOptions(int BeforeMinMinutes, int BeforeMaxMinutes, int AfterMinMinutes, int AfterMaxMinutes)
    {
        public static readonly WakeWindowOptions Default = new(5, 10, 5, 10);
    }

    // One queue entry as TimerManager sees it - just the facts the selection rule needs, so the
    // rule itself stays a pure function (no BaseTask/DI) and can be unit tested.
    public readonly record struct WakeQueueEntry(DateTime ExecuteAt, bool BypassOnlineHours, bool IsLogin, DateTime? WakeStart, DateTime? WakeEnd);

    // Pure rules for the "wake window" around a time-critical send (SendTroopsAtTimeTask with
    // a wake window). The problem it solves: a send scheduled for 03:00 never happens if the
    // account is in an "offline hour" (TimerManager skips every task) or in the middle of a
    // SleepTask (the queue is locked, browser closed) - see CHANGELOG.md 2026-10-03.
    public static class WakeWindowRules
    {
        private const int MaxMinutes = 600;

        // Shared window for all sends of one plan: starts BEFORE the earliest send, ends AFTER
        // the latest one, so a multi-village plan is covered by one wake-up.
        public static (DateTime Start, DateTime End) ComputeWindow(IEnumerable<DateTime> sendTimes, WakeWindowOptions options, Random random)
        {
            var times = sendTimes.ToList();
            if (times.Count == 0) throw new ArgumentException("At least one send time is required.", nameof(sendTimes));

            var before = RandomSpan(options.BeforeMinMinutes, options.BeforeMaxMinutes, random);
            var after = RandomSpan(options.AfterMinMinutes, options.AfterMaxMinutes, random);

            return (times.Min() - before, times.Max() + after);
        }

        // Window around an INCOMING attack's defensive actions (2026-10-03: Dodge, Defend+Donate,
        // and the recall that follows a dodge): opens `before` ahead of the FIRST action and
        // closes `after` the IMPACT. Never ends before the first action itself.
        public static (DateTime Start, DateTime End) ComputeAttackWindow(DateTime firstActionAt, DateTime impactAt, WakeWindowOptions options, Random random)
        {
            var before = RandomSpan(options.BeforeMinMinutes, options.BeforeMaxMinutes, random);
            var after = RandomSpan(options.AfterMinMinutes, options.AfterMaxMinutes, random);

            var end = impactAt + after;
            if (end < firstActionAt) end = firstActionAt + after;

            return (firstActionAt - before, end);
        }

        // Random pick in [min, max] minutes at SECOND resolution (so 5-10 doesn't always land
        // on whole minutes). Negative values count as 0, swapped min/max are fixed, and both
        // are capped so a typo can't schedule a day-long wake-up.
        public static TimeSpan RandomSpan(int minMinutes, int maxMinutes, Random random)
        {
            var lo = Math.Clamp(Math.Min(minMinutes, maxMinutes), 0, MaxMinutes);
            var hi = Math.Clamp(Math.Max(minMinutes, maxMinutes), 0, MaxMinutes);
            var seconds = random.Next(lo * 60, hi * 60 + 1);
            return TimeSpan.FromSeconds(seconds);
        }

        // True while at least one pending send's wake window has opened (its WakeStart has
        // passed) - the send is still in the queue, so it hasn't happened yet.
        public static bool HasOpenWindow(IEnumerable<DateTime?> wakeStarts, DateTime now)
        {
            return wakeStarts.Any(start => start is { } s && s <= now);
        }

        // Which queue entry TimerManager should run now (-1 = none). The queue is sorted by
        // ExecuteAt.
        //  * Online hour: exactly the old behavior - only the head, and only when it is due.
        //  * Offline hour: normally nothing runs. Exception: entries that bypass the offline
        //    gate (the wake-up task and the timed send itself), plus the LOGIN task while a
        //    wake window is open (the send's own pipeline re-adds a login first if the session
        //    expired overnight, and that login would otherwise be blocked forever).
        public static int SelectIndex(IReadOnlyList<WakeQueueEntry> queue, DateTime now, bool isOnlineHour)
        {
            if (queue.Count == 0) return -1;

            if (isOnlineHour) return queue[0].ExecuteAt <= now ? 0 : -1;

            var windowOpen = HasOpenWindow(queue.Select(e => e.WakeStart), now);

            for (var i = 0; i < queue.Count; i++)
            {
                var entry = queue[i];
                if (entry.ExecuteAt > now) break;
                if (entry.BypassOnlineHours || (windowOpen && entry.IsLogin)) return i;
            }

            return -1;
        }

        // After SleepTask was cut short by a wake window: when to sleep again. The end of the
        // latest pending window, but never sooner than 2 minutes from now - a send that is
        // already late must still go out BEFORE the sleep task (the login-retry path nudges a
        // task's ExecuteAt by a second, so a tighter bound could reorder them).
        // Returns null when no window is open (= normal work period applies).
        public static DateTime? NextSleepAfterWake(IEnumerable<(DateTime? WakeStart, DateTime? WakeEnd)> entries, DateTime now)
        {
            DateTime? latestEnd = null;
            var open = false;
            foreach (var (start, end) in entries)
            {
                if (start is not { } s || s > now) continue;
                open = true;
                if (end is { } e && (latestEnd is null || e > latestEnd)) latestEnd = e;
            }

            if (!open) return null;

            var earliest = now.AddMinutes(2);
            return latestEnd is { } le && le > earliest ? le : earliest;
        }
    }
}
