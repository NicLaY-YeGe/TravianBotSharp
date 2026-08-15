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
    // No enable/disable setting by design (2026-08-15 decision): the template is always the
    // embedded default and always gets applied to every new village. CanStart's own "does this
    // village already have jobs" check is what keeps this idempotent - it never reapplies, and
    // it never fires for a village that already has a build queue for any other reason.
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
                var alreadyHasJobs = context.Jobs.Any(x => x.VillageId == VillageId.Value);
                if (alreadyHasJobs) return false;

                var buildingsScanned = context.Buildings.Any(x => x.VillageId == VillageId.Value);
                return buildingsScanned;
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
