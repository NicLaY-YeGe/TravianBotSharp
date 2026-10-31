namespace MainCore.Commands.Features.Scheduling
{
    // 2026-10-30, user decision ("kisla ve ahirda asker hic bos kalmasin"): until now a training building was visited
    // AFTER its queue ended (queue end + 30-60 s buffer, QueueDeferRules), so every cycle left it idle for at least
    // that long, and a long offline stretch left it idle for hours. With a queue horizon the bot keeps the queue
    // filled AHEAD of time:
    //   - MinSeconds ("at least"): a visit is planned for the moment the queue has this much left;
    //   - TargetSeconds ("fill up to"): at a visit the queue is topped up until it lasts this long.
    //   0 / 0 = horizon off for that building (the old queue-end behaviour).
    //   - Offline hours: shortly before the offline stretch starts (PreOfflineLeadSeconds) the queue is filled so
    //     it lasts until the bot works again, but never beyond the user's offline cap. The cap can only EXTEND
    //     the fill; it never makes it shorter than the target.
    // Pure (no browser, no database), unit tested.
    public readonly record struct QueueHorizon(int MinSeconds, int TargetSeconds)
    {
        public bool Enabled => TargetSeconds > 0;

        // The settings are minutes; "at least" can never be above the target.
        public static QueueHorizon FromMinutes(int minMinutes, int targetMinutes)
        {
            var target = Math.Max(0, targetMinutes) * 60;
            var min = Math.Max(0, minMinutes) * 60;
            if (min > target) min = target;
            return new QueueHorizon(min, target);
        }
    }

    public static class QueueHorizonRules
    {
        // How long before an offline stretch the long fill happens (the last visit the bot is sure to make).
        public const int PreOfflineLeadSeconds = 10 * 60;

        // Two visits to one building are never closer than this.
        public const int MinVisitGapSeconds = 60;

        // A building that could not be filled (resources) is looked at again after this (random) time.
        public const int StarvedRetryMinSeconds = 5 * 60;
        public const int StarvedRetryMaxSeconds = 10 * 60;

        // How many seconds of work the queue should hold after this visit.
        public static int FillToSeconds(DateTime now, QueueHorizon horizon, DateTime? nextOfflineStart, DateTime? nextOnlineStart, int offlineCapSeconds)
        {
            var fillTo = horizon.TargetSeconds;
            if (offlineCapSeconds <= 0) return fillTo;
            if (nextOfflineStart is not { } offline || nextOnlineStart is not { } online) return fillTo;

            // Not close enough to the offline stretch yet: a normal fill, the visit is pulled to the lead point by NextVisit.
            if ((offline - now).TotalSeconds > PreOfflineLeadSeconds) return fillTo;

            var needed = (int)Math.Ceiling((online - now).TotalSeconds);
            if (needed <= fillTo) return fillTo;

            return Math.Max(fillTo, Math.Min(needed, offlineCapSeconds));
        }

        // Seconds of work still missing in the queue.
        public static int NeedSeconds(int fillToSeconds, int queueSeconds)
        {
            return Math.Max(0, fillToSeconds - Math.Max(0, queueSeconds));
        }

        // Units of one troop that cover `needSeconds` (rounded up so the queue never ends short). `perVisitCap` is
        // the troop's "amount max" setting: 0 = no cap. 0 units when the unit time is unknown.
        public static int UnitsFor(int needSeconds, int unitSeconds, int perVisitCap)
        {
            if (needSeconds <= 0 || unitSeconds <= 0) return 0;

            var units = (int)Math.Ceiling(needSeconds / (double)unitSeconds);
            if (perVisitCap > 0) units = Math.Min(units, perVisitCap);
            return Math.Max(0, units);
        }

        // When to come back: when the queue has `minSeconds` left. If that moment falls into (or right in front of) an
        // offline stretch, come back at the lead point instead so the long fill is done as late as possible.
        // Never sooner than MinVisitGapSeconds from now.
        public static DateTime NextVisit(DateTime now, int queueSeconds, int minSeconds, DateTime? nextOfflineStart)
        {
            var planned = now.AddSeconds(Math.Max(0, queueSeconds - Math.Max(0, minSeconds)));
            var floor = now.AddSeconds(MinVisitGapSeconds);

            if (nextOfflineStart is { } offline)
            {
                var lead = offline.AddSeconds(-PreOfflineLeadSeconds);
                if (planned > lead && lead > floor) planned = lead;
            }

            return planned < floor ? floor : planned;
        }

        public static int StarvedRetrySeconds(Random random)
        {
            return random.Next(StarvedRetryMinSeconds, StarvedRetryMaxSeconds + 1);
        }
    }
}
