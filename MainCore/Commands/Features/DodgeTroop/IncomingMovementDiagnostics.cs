using System.Collections.Concurrent;
using MainCore.Parsers;
using MainCore.Services;
using static MainCore.Parsers.RallyPointOverviewParser;

namespace MainCore.Commands.Features.DodgeTroop
{
    // 2026-10-23: Dodge and Defend used to skip SILENTLY whenever the village was flagged "under
    // attack" but the Rally Point Overview showed no inAttack table (live log 2026-10-22: 14 silent
    // skips in 3.5 hours; the page can also hold an inRaid table - see RallyPointOverviewParser).
    // This turns every such skip into a log line that says what the page really contained, and saves
    // the page (at most once per village per 30 minutes, so a flag that stays on cannot fill the disk).
    public static class IncomingMovementDiagnostics
    {
        private static readonly ConcurrentDictionary<int, DateTime> LastDump = new();
        private static readonly TimeSpan DumpInterval = TimeSpan.FromMinutes(30);

        public static string Describe(IReadOnlyCollection<IncomingMovement> movements)
        {
            if (movements.Count == 0) return "none";
            return string.Join("; ", movements.Select(m => $"{m.Kind} in {m.Seconds}s ({m.Headline})"));
        }

        // Call when a defensive task found nothing to react to although the village is flagged.
        // `wanted` is what the task would have reacted to; `all` is everything hostile on the page.
        public static async Task ReportNothingToDoAsync(
            IChromeBrowser browser,
            ILogger logger,
            VillageId villageId,
            string taskName,
            IReadOnlyCollection<IncomingMovement> all)
        {
            if (all.Count > 0)
            {
                // Something hostile is incoming, it is just not what this task reacts to.
                logger.Information(
                    "{Task} on {VillageId}: incoming movement(s) found but none to react to with the current settings: {Movements}.",
                    taskName, villageId, Describe(all));
                return;
            }

            logger.Warning(
                "{Task} on {VillageId}: the village is flagged under attack but the Rally Point Overview shows no incoming attack or raid (stale flag, or a movement type the parser does not know).",
                taskName, villageId);

            var now = DateTime.Now;
            if (LastDump.TryGetValue(villageId.Value, out var last) && now - last < DumpInterval) return;
            LastDump[villageId.Value] = now;
            await PageDump.SaveAsync(browser, logger, $"no-incoming-{villageId.Value}");
        }
    }
}
