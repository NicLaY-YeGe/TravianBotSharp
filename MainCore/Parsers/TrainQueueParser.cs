namespace MainCore.Parsers
{
    // Reads the countdown of the game's "under progress" table - the queue box that Barracks /
    // Stable / Workshop (training), the Trapper (traps), the Smithy (research) and the Town Hall
    // (celebration) show above their forms.
    //
    // CHECKED against real captures: only the Smithy and Town Hall variants (see SmithyParser /
    // TownHallParser, 2026-07/08). NOT checked: the training and trap queue - the repository has no
    // capture of a Barracks / Stable / Trapper page with an ACTIVE queue (the fixtures are idle
    // pages). Two things are therefore assumptions, and callers must stay safe if they are wrong:
    //   1. the queue sits in `table.under_progress` (same class the Smithy page uses);
    //   2. the FIRST timer in it is the batch that is running now, so its value is when the next
    //      batch finishes. When the queue holds several batches this is NOT the end of the whole
    //      queue - which is why TrainTroopTask only adds a batch when the queue is empty (then the
    //      first timer IS the end), and why its waits are capped (QueueDeferRules).
    // Timer markup also differs between captures: `value="123"` (Sept 2024 Barracks page) and
    // `data-value="123"` (the Smithy parser's capture), so both are accepted.
    public static class TrainQueueParser
    {
        // A longer countdown than this is not a training/queue timer but a misread (30 days).
        private const int MaxPlausibleSeconds = 30 * 24 * 60 * 60;

        // Seconds until the running batch finishes; null when there is no queue table or timer.
        public static int? GetSecondsRemaining(HtmlDocument doc)
        {
            var table = doc.DocumentNode
                .Descendants("table")
                .FirstOrDefault(x => x.HasClass("under_progress"));
            if (table is null) return null;

            foreach (var span in table.Descendants("span").Where(x => x.HasClass("timer")))
            {
                var seconds = GetTimerSeconds(span);
                if (seconds is not null) return seconds;
            }

            return null;
        }

        // True when the page has the queue table at all (even with no readable timer) - logged by the
        // tasks that cannot read a running queue, so the next live log shows whether the markup or
        // the timer is what the parser misses.
        public static bool HasQueueTable(HtmlDocument doc)
        {
            return doc.DocumentNode
                .Descendants("table")
                .Any(x => x.HasClass("under_progress"));
        }

        // How many countdowns the queue table holds - logged next to the value so the next live
        // log shows what a multi-batch queue really looks like.
        public static int CountTimers(HtmlDocument doc)
        {
            var table = doc.DocumentNode
                .Descendants("table")
                .FirstOrDefault(x => x.HasClass("under_progress"));
            if (table is null) return 0;

            return table.Descendants("span").Count(x => x.HasClass("timer"));
        }

        // Remaining seconds of one `span.timer` (value or data-value attribute), null if unusable.
        public static int? GetTimerSeconds(HtmlNode timer)
        {
            var raw = timer.GetAttributeValue("value", "");
            if (string.IsNullOrWhiteSpace(raw)) raw = timer.GetAttributeValue("data-value", "");
            if (!int.TryParse(raw.Trim(), out var seconds)) return null;
            if (seconds <= 0 || seconds > MaxPlausibleSeconds) return null;
            return seconds;
        }
    }
}
