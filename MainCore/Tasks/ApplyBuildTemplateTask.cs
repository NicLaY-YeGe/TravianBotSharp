using MainCore.Commands.UI.Villages.BuildViewModel;
using MainCore.Tasks.Base;

namespace MainCore.Tasks
{
    // Applies DefaultBuildTemplate.Get() to a freshly founded village, once - see
    // CLAUDE.md/PROJECT_CONTEXT.md §5l. Queued by UpdateVillageListCommand as soon as a new
    // village row appears in the DB, but CanStart deliberately waits until that village's
    // Buildings have actually been scanned (a brand-new village has ZERO Building rows until
    // UpdateBuildingTask runs a live page scan - EnableAutoLoadVillageBuilding triggers that
    // automatically, same as for any other village missing its building layout). Without that
    // wait, FixJobsCommand would find no "Site" placeholders to match locations against and
    // filter out every single job.
    //
    // "Scanned" specifically means location 40 (the wall plot) is present, not just any
    // building row: dorf1 (resource fields) and dorf2 (village center) are scanned as separate
    // pages, so a village can have resource-field rows well before its village-center rows
    // (main building/rally point/wall, locations 26/39/40) exist. FixJobsCommand.Modify reads
    // location 40 for every wall-type job in the template, so we wait for that specific
    // location rather than "any row at all" (2026-08-25 fix - see CHANGELOG).
    //
    // Applies DefaultBuildTemplate.Get() to a freshly founded village, once - see
    // CLAUDE.md/PROJECT_CONTEXT.md §5l. Queued by UpdateVillageListCommand as soon as a new
    // village row appears in the DB, but CanStart deliberately waits until that village's
    // Buildings have actually been scanned (a brand-new village has ZERO Building rows until
    // UpdateBuildingTask runs a live page scan - EnableAutoLoadVillageBuilding triggers that
    // automatically, same as for any other village missing its building layout). Without that
    // wait, FixJobsCommand would find no "Site" placeholders to match locations against and
    // filter out every single job.
    //
    // "Scanned" specifically means location 40 (the wall plot) is present, not just any
    // building row: dorf1 (resource fields) and dorf2 (village center) are scanned as separate
    // pages, so a village can have resource-field rows well before its village-center rows
    // (main building/rally point/wall, locations 26/39/40) exist. FixJobsCommand.Modify reads
    // location 40 for every wall-type job in the template, so we wait for that specific
    // location rather than "any row at all" (2026-08-25 fix - see CHANGELOG).
    //
    // Was hardcoded always-on with no enable/disable setting by design (2026-08-15 decision).
    // 2026-09-12: made opt-out via VillageSettingEnums.AutoApplyBuildTemplateEnable (default
    // ON, so existing behavior is unchanged unless someone explicitly turns it off).
    // CanStart's own "does this village already have jobs" check still keeps this idempotent
    // regardless of the setting - it never reapplies, and it never fires for a village that
    // already has a build queue for any other reason.
    [Handler]
    public static partial class ApplyBuildTemplateTask
    {
        public sealed class Task : VillageTask
        {
            public Task(AccountId accountId, VillageId villageId) : base(accountId, villageId)
            {
            }

            protected override string TaskName => "Apply default build template";

            public override bool CanStart(AppDbContext context)
            {
                // A village that has never explicitly saved this setting has no row for it at
                // all - GetValueOrDefault/BooleanByName would silently read that as "disabled",
                // which is backwards for an opt-OUT toggle meant to leave every existing
                // village's behavior unchanged. Only treat it as disabled when a row genuinely
                // says so.
                var hasExplicitSetting = context.VillagesSetting.Any(x =>
                    x.VillageId == VillageId.Value && x.Setting == VillageSettingEnums.AutoApplyBuildTemplateEnable);
                if (hasExplicitSetting)
                {
                    var enabled = context.BooleanByName(VillageId, VillageSettingEnums.AutoApplyBuildTemplateEnable);
                    if (!enabled) return false;
                }

                var alreadyHasJobs = context.Jobs.Any(x => x.VillageId == VillageId.Value);
                if (alreadyHasJobs) return false;

                var wallLocationScanned = context.Buildings.Any(x => x.VillageId == VillageId.Value && x.Location == 40);
                return wallLocationScanned;
            }
        }

        private static async ValueTask<Result> HandleAsync(
            Task task,
            AppDbContext context,
            FixJobsCommand.Handler fixJobsCommand,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            var template = DefaultBuildTemplate.Get();
            if (template.Count == 0)
            {
                logger.Warning("Default build template is empty or could not be loaded - nothing to apply to village {VillageId}.", task.VillageId);
                return Result.Ok();
            }

            // FixJobsCommand mutates the JobDto instances it's given (see its own code) -
            // clone so the cached template stays untouched for the next village.
            var jobsCopy = template
                .Select(x => new JobDto { Id = x.Id, Position = x.Position, Type = x.Type, Content = x.Content })
                .ToList();

            var fixedJobs = await fixJobsCommand.HandleAsync(new(task.VillageId, jobsCopy, Shuffle: false), cancellationToken);

            var additionJobs = fixedJobs
                .Select((job, index) => new Job()
                {
                    Position = index,
                    VillageId = task.VillageId.Value,
                    Type = job.Type,
                    Content = job.Content,
                })
                .ToList();

            context.AddRange(additionJobs);
            context.SaveChanges();

            logger.Information(
                "Applied default build template to newly founded village {VillageId}: {Count}/{Total} jobs matched the village's layout.",
                task.VillageId, additionJobs.Count, template.Count);

            return Result.Ok();
        }
    }
}
