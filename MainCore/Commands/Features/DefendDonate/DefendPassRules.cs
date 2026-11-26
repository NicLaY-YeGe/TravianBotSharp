namespace MainCore.Commands.Features.DefendDonate
{
    // Which Defend pass comes next, and when (pure, unit tested).
    //   pass 1 - when the movement is first seen;
    //   pass 2 - 15 min before impact (2026-10-24), only if that moment is still ahead;
    //   pass 3 - the FINAL pass, shortly before Dodge's send moment (2026-11-26).
    // Why pass 3: live log 2026-11-26 04:26-04:38 - the attack was seen 12 min before impact, so pass 2 (15 min before)
    // never existed, and nothing donated what arrived afterwards. Shipments already on their way (and normal production)
    // were plundered. Pass 3 trains/donates once more right before the attack, under the same time budget rules, so
    // it ends before Dodge sends the troops out.
    public static class DefendPassRules
    {
        public const int SecondPassLeadSeconds = 15 * 60;

        // A pass is only scheduled if it starts at least this long after "now" (the pass that is ending).
        public const int MinGapSeconds = 60;

        // The final pass starts this long before the moment the reserve for Dodge / the impact begins.
        public const int FinalPassExtraSeconds = 60;

        // Seconds before impact at which the final pass starts.
        // With Dodge: dodge send lead + 60 s (the pass budget is then 45 s: it ends 15 s before Dodge's send moment).
        // Without Dodge: 15 s reserve + 60 s.
        public static int FinalPassLeadSeconds(bool dodgeWillRun, int dodgeLeadSeconds)
        {
            var reserve = dodgeWillRun ? (dodgeLeadSeconds <= 0 ? 30 : dodgeLeadSeconds) : 15;
            return reserve + FinalPassExtraSeconds;
        }

        // Returns the pass to schedule and its start time, or null when no further pass fits.
        public static (int Pass, DateTime At)? Next(int currentPass, DateTime now, DateTime impactAt, int finalPassLeadSeconds)
        {
            var earliest = now.AddSeconds(MinGapSeconds);

            if (currentPass <= 1)
            {
                var second = impactAt.AddSeconds(-SecondPassLeadSeconds);
                if (second > earliest) return (2, second);
            }

            if (currentPass <= 2)
            {
                var final = impactAt.AddSeconds(-finalPassLeadSeconds);
                if (final > earliest) return (3, final);
            }

            return null;
        }
    }
}
