namespace MainCore.Parsers
{
    // Reads the countdown of the game's "under progress" table - the queue box that Barracks /
    // Stable / Workshop (training), the Trapper (traps), the Smithy (research) and the Town Hall
    // (celebration) show above their forms.
    //
    // CHECKED against real captures: the Smithy and Town Hall variants (see SmithyParser /
    // TownHallParser, 2026-07/08) and, since 2026-10-25, a REAL Barracks page with an active queue
    // (fixture Parsers/TrainQueue/BarracksActiveQueue.html, two batches: 1 Swordsman 419 s, 5
    // Swordsmen 3153 s). What that capture showed:
    //   1. the queue is `table.under_progress`, one row per batch, the countdown is `td.dur > span.timer`;
    //   2. the batch timers are CUMULATIVE: 3153 = 419 + 5 x 547 s, i.e. each row's timer is the time
    //      until THAT batch is finished, so the LAST row's timer is the end of the whole queue;
    //   3. the extra row `tr.next` ("Next soldier will be ready in <span class=timer>") repeats the
    //      first unit's time and is not in a `td.dur` cell - it must not be taken for the queue end.
    // GetSecondsRemaining therefore returns the largest timer found in `td.dur` cells (= the end of the
    // whole queue); a table without `td.dur` cells (the Smithy / Town Hall variants) falls back to the
    // first timer in the table. NOT checked: the Trapper's queue (no capture of an active one).
    // Timer markup differs between captures: `value="123"` and `data-value="123"` (the real Barracks
    // page carries both), so both are accepted.
    public static class TrainQueueParser
    {
        // A longer countdown than this is not a training/queue timer but a misread (30 days).
        private const int MaxPlausibleSeconds = 30 * 24 * 60 * 60;

        // Seconds until the whole queue is finished (see the header: the last batch's cumulative timer);
        // null when there is no queue table or no readable timer.
        public static int? GetSecondsRemaining(HtmlDocument doc)
        {
            var table = doc.DocumentNode
                .Descendants("table")
                .FirstOrDefault(x => x.HasClass("under_progress"));
            if (table is null) return null;

            var batchTimers = table.Descendants("td")
                .Where(x => x.HasClass("dur"))
                .SelectMany(x => x.Descendants("span").Where(s => s.HasClass("timer")))
                .Select(GetTimerSeconds)
                .Where(x => x is not null)
                .Select(x => x!.Value)
                .ToList();
            if (batchTimers.Count > 0) return batchTimers.Max();

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
