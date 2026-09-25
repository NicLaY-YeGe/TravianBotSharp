using MainCore.Commands.Features.CropScan;
using MainCore.Commands.Features.OasisScout;
using MainCore.Commands.NextExecute;
using MainCore.Tasks.Base;

namespace MainCore.Tasks
{
    // Crop oasis scan (2026-09-24, user request): given a center coordinate (village settings,
    // manually entered by the user - NOT necessarily this village's own position) and a max
    // distance, walks every integer coordinate within that radius, nearest-first
    // (CropScanCoordinateRules), opens each on the map (reusing OasisScout's ToMapTileCommand -
    // jump to X/Y, click the viewport), and logs any "Abandoned valley" tile whose Croplands
    // count is >= the configured minimum to a CSV file.
    //
    // Deliberately a ONE-SHOT task, not a recurring one: unlike every other VillageTask here,
    // it does NOT reschedule forever - once the coordinate queue is empty it returns Ok without
    // touching ExecuteAt, which (per the scheduler's own lifecycle) removes it from the queue.
    // While it's still running, each cycle processes exactly ONE coordinate then reschedules
    // itself CropScanGapMin/MaxSeconds later - explicit user requirement (2026-09-24): the bot
    // must not hammer the map dialog open/closed back-to-back like a script, it has to pace
    // itself between tiles the same randomized way RaidListTask paces its own sends.
    //
    // The coordinate queue and CSV path are built ONCE, lazily, on the task's first run, and
    // kept on the TASK INSTANCE itself (not re-read from settings every cycle) - the same
    // instance is re-invoked by the scheduler every cycle (see BaseTask.ExecuteAt), so this is
    // safe, and it means a scan keeps its original parameters even if the user edits the
    // village's crop-scan settings again while an earlier scan is still in flight.
    //
    // Failures opening ONE tile are logged and skipped, same philosophy as OasisScoutTask - an
    // unattended few-hundred-tile scan having one bad tile is routine, not worth pausing the
    // whole bot over.
    [Handler]
    public static partial class CropOasisScanTask
    {
        public sealed class Task : VillageTask
        {
            internal Queue<(int X, int Y, double Distance)>? Remaining;
            internal string? CsvPath;
            internal int MinCroplands;
            internal int TotalQueued;
            internal int TotalFound;

            public Task(AccountId accountId, VillageId villageId) : base(accountId, villageId)
            {
            }

            protected override string TaskName => "Crop oasis scan";

            public override bool CanStart(AppDbContext context)
            {
                return context.BooleanByName(VillageId, VillageSettingEnums.CropScanEnable);
            }
        }

        private static async ValueTask<Result> HandleAsync(
            Task task,
            AppDbContext context,
            ISettingService settingService,
            IChromeBrowser browser,
            ToMapTileCommand.Handler toMapTileCommand,
            NextExecuteCropOasisScanTaskCommand.Handler nextExecuteCropOasisScanTaskCommand,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            var villageId = task.VillageId;

            if (task.Remaining is null)
            {
                var centerX = settingService.ByName(villageId, VillageSettingEnums.CropScanCenterX);
                var centerY = settingService.ByName(villageId, VillageSettingEnums.CropScanCenterY);
                var maxDistance = settingService.ByName(villageId, VillageSettingEnums.CropScanMaxDistance);
                task.MinCroplands = settingService.ByName(villageId, VillageSettingEnums.CropScanMinCroplands);

                var coordinates = CropScanCoordinateRules.GetCoordinatesByDistance(centerX, centerY, maxDistance);
                task.Remaining = new Queue<(int, int, double)>(coordinates);
                task.TotalQueued = coordinates.Count;

                var dir = Path.Combine(AppContext.BaseDirectory, "CropScans");
                Directory.CreateDirectory(dir);
                task.CsvPath = Path.Combine(dir, $"CropScan_village{villageId.Value}_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                await File.WriteAllTextAsync(task.CsvPath, "X,Y,Croplands,Distance" + Environment.NewLine, cancellationToken);

                logger.Information(
                    "Crop scan: starting from ({X}|{Y}), max distance {MaxDistance}, min croplands {MinCroplands}, {Count} tiles queued. Results: {Path}",
                    centerX, centerY, maxDistance, task.MinCroplands, task.TotalQueued, task.CsvPath);
            }

            if (task.Remaining.Count == 0)
            {
                logger.Information(
                    "Crop scan: finished - checked {Total} tiles, found {Found}. Results: {Path}",
                    task.TotalQueued, task.TotalFound, task.CsvPath);

                // Auto-unticks the "Start scan" checkbox in the DB so a later login/restart
                // doesn't silently re-run a scan the user already saw finish (this task is
                // deliberately NOT in RxQueue's bootstrap list for that same reason - it only
                // ever starts from SaveVillageSettingCommand reacting to the checkbox, never on
                // login). If the bot restarts WHILE a scan is still in progress, this in-memory
                // queue is lost and the checkbox stays on - a full re-scan on next login is the
                // safe (if wasteful) fallback there, not silent data loss.
                context.VillagesSetting
                    .Where(x => x.VillageId == task.VillageId.Value)
                    .Where(x => x.Setting == VillageSettingEnums.CropScanEnable)
                    .ExecuteUpdate(x => x.SetProperty(x => x.Value, 0));

                // No NextExecute call: a task that returns Ok without moving ExecuteAt forward
                // is removed from the queue - this is the intended end of a one-shot scan, not
                // a recurring task like every other CanStart-gated VillageTask in this project.
                return Result.Ok();
            }

            var (x, y, distance) = task.Remaining.Dequeue();

            var tileResult = await toMapTileCommand.HandleAsync(new(x, y), cancellationToken);
            if (tileResult.IsFailed)
            {
                logger.Warning("Crop scan: could not open tile ({X}|{Y}) - {Error}", x, y, tileResult.Errors.FirstOrDefault()?.Message);
            }
            else if (CropOasisTileParser.IsAbandonedValley(browser.Html))
            {
                var croplands = CropOasisTileParser.GetCroplands(browser.Html);
                if (croplands.HasValue && croplands.Value >= task.MinCroplands)
                {
                    await File.AppendAllTextAsync(
                        task.CsvPath!,
                        $"{x},{y},{croplands.Value},{distance:F2}{Environment.NewLine}",
                        cancellationToken);
                    task.TotalFound++;
                    logger.Information(
                        "Crop scan: found {Croplands}-crop oasis at ({X}|{Y}), {Distance:F2} fields away.",
                        croplands.Value, x, y, distance);
                }
            }

            logger.Information("Crop scan: checked ({X}|{Y}), {Remaining} tiles left.", x, y, task.Remaining.Count);

            await nextExecuteCropOasisScanTaskCommand.HandleAsync(new(task), cancellationToken);
            return Result.Ok();
        }
    }
}
