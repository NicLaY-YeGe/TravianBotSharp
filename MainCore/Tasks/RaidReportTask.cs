using MainCore.Commands.Features.RaidReport;
using MainCore.Commands.NextExecute;
using MainCore.Tasks.Base;

namespace MainCore.Tasks
{
    // "Yagma organize", scope A (2026-09-19, user request): the Raid List used to send blindly -
    // nothing ever looked at what came back. This task reads /report/offensive, matches every NEW
    // raid report to the Raid List row it belongs to (by target coordinates, and by source
    // village where the same coordinates appear in several villages' rows), records the result
    // on the row (RaidListEntry.ReportStatsJson, shown in the Raid List tab) and pauses a row
    // that keeps costing troops (RaidReportRules.GetPauseReason: a lost raid at once, or two
    // lossy ones in a row) - only THAT row (IsActive = false + its queued task removed, same
    // effect as the row's own toggle), never the whole list or the whole bot.
    //
    // Design points worth knowing (all from real captures, 2026-09-19):
    //  - The list page alone already carries the result icon, the loot ("42/225") and - for
    //    oases and for villages with coordinates in their name - the target. For ordinary player
    //    villages the list only shows the village NAME, so those reports are opened (one page
    //    load each, at most MaxDetailPagesPerRun per run) and the target is decoded from the
    //    defender village's map tile id (MapTiles); the map radius is inferred from OUR village's
    //    known coordinates, so nothing has to be configured.
    //  - "New" means report id > AccountSettingEnums.RaidReportLastId (ids grow with time). The
    //    read/unread flag is useless here - the user reading a report by hand flips it.
    //  - The FIRST run (RaidReportLastId == 0) evaluates NOTHING: it only remembers the newest
    //    report id as the starting point, so history that predates this feature can never pause
    //    a row. Statistics therefore start with the first raid sent after that.
    //  - Reports for coordinates nothing in the Raid List targets (Oasis Scout raids, raids the
    //    user sent by hand) are simply not ours and are skipped without opening them.
    //  - Loot percentages (full / low streaks) are recorded and shown, never acted on: in the
    //    first real capture 5 of 8 raids carried under 30% and none had losses.
    //
    // TASK LIFECYCLE: an AccountTask, re-armed by NextExecuteRaidReportTaskCommand after every
    // normal run (per TimerManager an unchanged ExecuteAt would REMOVE it). With no active
    // Raid List row it does not touch the browser at all. A failure to load a report page
    // propagates (Retry-class, like OasisScoutTask's report page) - but progress made so far is
    // already saved, so the retry continues where this run stopped.
    [Handler]
    public static partial class RaidReportTask
    {
        public sealed class Task : AccountTask
        {
            public Task(AccountId accountId) : base(accountId)
            {
            }

            protected override string TaskName => "Raid report";

            public override bool CanStart(AppDbContext context)
            {
                return context.BooleanByName(AccountId, AccountSettingEnums.EnableRaidReport);
            }
        }

        // 10 reports per page; a 15-25 minute cycle rarely sees more than a handful of new ones.
        private const int MaxListPages = 3;

        // Every report to an ordinary player village costs a page load - cap it per run and
        // come back sooner (see BacklogRecheckMinutes) instead of browsing for minutes at once.
        private const int MaxDetailPagesPerRun = 6;
        private const int BacklogRecheckMinutesMin = 2;
        private const int BacklogRecheckMinutesMax = 4;

        private static async ValueTask<Result> HandleAsync(
            Task task,
            AppDbContext context,
            IChromeBrowser browser,
            ITaskManager taskManager,
            ITelegramNotifier telegramNotifier,
            ToOffensiveReportPageCommand.Handler toOffensiveReportPageCommand,
            ToOffensiveReportDetailCommand.Handler toOffensiveReportDetailCommand,
            NextExecuteRaidReportTaskCommand.Handler nextExecuteRaidReportTaskCommand,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            var accountId = task.AccountId;

            var activeEntries = context.RaidListEntries
                .Where(x => x.AccountId == accountId.Value && x.IsActive)
                .ToList();
            if (activeEntries.Count == 0)
            {
                await nextExecuteRaidReportTaskCommand.HandleAsync(new(task), cancellationToken);
                return Result.Ok();
            }

            var lastReportId = context.ByName(accountId, AccountSettingEnums.RaidReportLastId);
            if (lastReportId <= 0)
            {
                // First run - see the class comment: remember where the list stands now and
                // evaluate nothing. (If the list is empty there is nothing to remember yet and
                // the next run is the "first" run again.)
                var firstPageResult = await toOffensiveReportPageCommand.HandleAsync(new(1), cancellationToken);
                if (firstPageResult.IsFailed) return firstPageResult;

                var newest = OffensiveReportParser.GetReportRows(browser.Html)
                    .Select(x => x.ReportId)
                    .DefaultIfEmpty(0L)
                    .Max();
                if (newest > 0)
                {
                    SaveLastReportId(context, accountId, newest);
                    logger.Information(
                        "Raid report: first run - report {ReportId} is the starting point, only newer reports are evaluated.",
                        newest);
                }

                await nextExecuteRaidReportTaskCommand.HandleAsync(new(task), cancellationToken);
                return Result.Ok();
            }

            // 1. Collect every report newer than what was handled before, newest page first,
            //    stopping as soon as a page reaches already-handled reports (or the list ends).
            var newRows = new List<OffensiveReportRow>();
            for (var page = 1; page <= MaxListPages; page++)
            {
                var pageResult = await toOffensiveReportPageCommand.HandleAsync(new(page), cancellationToken);
                if (pageResult.IsFailed) return pageResult;

                var rows = OffensiveReportParser.GetReportRows(browser.Html);
                var fresh = rows.Where(x => x.ReportId > lastReportId).ToList();
                newRows.AddRange(fresh);

                if (fresh.Count < rows.Count) break;
                if (!OffensiveReportParser.HasNextPage(browser.Html)) break;
            }

            if (newRows.Count == 0)
            {
                await nextExecuteRaidReportTaskCommand.HandleAsync(new(task), cancellationToken);
                return Result.Ok();
            }

            // Oldest first, so RaidReportLastId only ever advances over reports that were
            // really handled - a run that stops early leaves the rest for the next one.
            // (DistinctBy: a report that arrives between two page loads shifts the list by one, so
            // the same row can show up on both pages.)
            var ordered = newRows.DistinctBy(x => x.ReportId).OrderBy(x => x.ReportId).ToList();

            var villages = context.Villages
                .Where(x => x.AccountId == accountId.Value)
                .ToList()
                .Select(x => (VillageId: x.Id, x.X, x.Y))
                .ToList();

            var pausedRows = new List<(RaidListEntry Entry, string Reason)>();
            var detailBudget = MaxDetailPagesPerRun;
            var hasBacklog = false;

            foreach (var row in ordered)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (row.Outcome is < 1 or > 3)
                {
                    // A result icon this version does not know - nothing to learn from it.
                    SaveLastReportId(context, accountId, row.ReportId);
                    continue;
                }

                List<RaidListEntry> matches;
                if (row.HasCoordinates)
                {
                    matches = RaidReportRules.MatchEntries(activeEntries, row.TargetX, row.TargetY, null);
                    if (matches.Count == 0)
                    {
                        // Not a Raid List target (Oasis Scout, or a raid sent by hand).
                        SaveLastReportId(context, accountId, row.ReportId);
                        continue;
                    }
                }
                else
                {
                    matches = new List<RaidListEntry>();
                }

                if (!row.HasCoordinates || RaidReportRules.SpansSeveralVillages(matches))
                {
                    if (detailBudget <= 0)
                    {
                        hasBacklog = true;
                        break;
                    }
                    detailBudget--;

                    var detailResult = await toOffensiveReportDetailCommand.HandleAsync(new(row.DetailHref), cancellationToken);
                    if (detailResult.IsFailed) return detailResult;

                    var detail = OffensiveReportParser.ParseDetail(browser.Html);
                    var resolved = detail is null ? null : ResolveTarget(detail, row, villages);
                    if (resolved is null)
                    {
                        logger.Warning(
                            "Raid report {ReportId}: could not tell which of your villages sent it / where it went - skipping it.",
                            row.ReportId);
                        SaveLastReportId(context, accountId, row.ReportId);
                        continue;
                    }

                    var (targetX, targetY, sourceVillageId) = resolved.Value;
                    matches = RaidReportRules.MatchEntries(activeEntries, targetX, targetY, sourceVillageId);
                    if (matches.Count == 0)
                    {
                        SaveLastReportId(context, accountId, row.ReportId);
                        continue;
                    }
                }

                var lootPercent = row.HasLootInfo ? RaidReportRules.LootPercent(row.LootCarried, row.LootCapacity) : -1;

                foreach (var entry in matches)
                {
                    var previous = entry.GetReportStats();
                    var updated = RaidReportRules.Apply(previous, row.ReportId, row.Outcome, lootPercent);
                    if (ReferenceEquals(updated, previous)) continue;

                    entry.SetReportStats(updated);

                    logger.Information(
                        "Raid report: village {VillageId} -> ({X}|{Y}): {Summary}",
                        entry.VillageId, entry.TargetX, entry.TargetY, RaidReportRules.Summarize(updated));

                    var reason = RaidReportRules.GetPauseReason(updated);
                    if (reason is null) continue;

                    PauseEntry(entry, accountId, taskManager);
                    activeEntries.Remove(entry);
                    pausedRows.Add((entry, reason));
                }

                context.SaveChanges();
                SaveLastReportId(context, accountId, row.ReportId);
            }

            foreach (var (entry, reason) in pausedRows)
            {
                logger.Warning(
                    "Raid report: pausing the raid list row village {VillageId} -> ({X}|{Y}) - {Reason}.",
                    entry.VillageId, entry.TargetX, entry.TargetY, reason);

                var telegramSetting = telegramNotifier.Get(accountId);
                if (telegramSetting.NotifyOnPause)
                {
                    var username = context.Accounts.FirstOrDefault(x => x.Id == accountId.Value)?.Username ?? $"{accountId}";
                    await telegramNotifier.NotifyAsync(
                        accountId,
                        $"\u26D4 {username} - yagma satiri durduruldu ({entry.TargetX}|{entry.TargetY}): {reason}",
                        cancellationToken);
                }
            }

            if (hasBacklog)
            {
                task.ExecuteAt = DateTime.Now.AddMinutes(Random.Shared.Next(BacklogRecheckMinutesMin, BacklogRecheckMinutesMax + 1));
            }
            else
            {
                await nextExecuteRaidReportTaskCommand.HandleAsync(new(task), cancellationToken);
            }

            return Result.Ok();
        }

        // Works out (target X, target Y, source village id) for a report whose list row did not
        // say where it went: which of OUR villages sent it (and with that the map radius), then
        // the defender's position - straight from the report when it prints coordinates (oases),
        // otherwise decoded from its tile id. Null = could not be resolved reliably.
        private static (int X, int Y, int SourceVillageId)? ResolveTarget(
            OffensiveReportDetail detail,
            OffensiveReportRow row,
            IReadOnlyList<(int VillageId, int X, int Y)> villages)
        {
            var attacker = RaidReportRules.ResolveAttackerVillage(detail.AttackerTileId, villages);
            if (attacker is null) return null;

            if (row.HasCoordinates) return (row.TargetX, row.TargetY, attacker.Value.VillageId);
            if (detail.HasDefenderCoordinates) return (detail.DefenderX, detail.DefenderY, attacker.Value.VillageId);
            if (detail.DefenderTileId <= 0) return null;

            var decoded = MapTiles.ToCoordinates(detail.DefenderTileId, attacker.Value.Radius);
            if (decoded is null) return null;

            return (decoded.Value.X, decoded.Value.Y, attacker.Value.VillageId);
        }

        // Same effect as the row's own toggle in the Raid List tab and as RaidListTask's
        // PauseWholeListAndNotify does per row: IsActive = false in the DB (the caller saves)
        // and the row's queued RaidListTask removed so it cannot fire once more.
        private static void PauseEntry(RaidListEntry entry, AccountId accountId, ITaskManager taskManager)
        {
            entry.IsActive = false;

            var queuedTask = taskManager.GetTaskList(accountId)
                .OfType<RaidListTask.Task>()
                .FirstOrDefault(t => t.EntryId.Value == entry.Id);
            if (queuedTask is not null) taskManager.Remove(accountId, queuedTask);
        }

        private static void SaveLastReportId(AppDbContext context, AccountId accountId, long reportId)
        {
            var value = (int)Math.Min(reportId, int.MaxValue);

            var setting = context.AccountsSetting.FirstOrDefault(x =>
                x.AccountId == accountId.Value && x.Setting == AccountSettingEnums.RaidReportLastId);

            if (setting is null)
            {
                context.AccountsSetting.Add(new AccountSetting
                {
                    AccountId = accountId.Value,
                    Setting = AccountSettingEnums.RaidReportLastId,
                    Value = value,
                });
            }
            else
            {
                setting.Value = value;
            }

            context.SaveChanges();
        }
    }
}
