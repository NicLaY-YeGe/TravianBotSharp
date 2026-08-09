namespace MainCore.Commands.Features.UpgradeBuilding
{
    // Called by GetBuildPlanCommand when ValidatePlanCompleteCommand reports a missing
    // prerequisite (e.g. Bakery needs GrainMill level 5) that isn't already under
    // construction. Queues a job to work towards that prerequisite - one level at a time,
    // same as the rest of the build queue - so the outer loop in GetBuildPlanCommand can
    // pick it up next (it's inserted at the top of the queue) and, if that prerequisite has
    // prerequisites of its own, the same mechanism resolves those too on a later iteration.
    [Handler]
    public static partial class ResolveMissingPrerequisiteCommand
    {
        public sealed record Command(VillageId VillageId, BuildingEnums PrerequisiteType, int PrerequisiteLevel) : IVillageCommand;

        // Non-resource-field plots span 19..39; 40 is reserved for the Wall and is never
        // picked automatically (see NormalBuildCommand.ValidateLocation). Prerequisite
        // buildings are always infrastructure buildings (never a resource field), so any
        // empty plot in this range works.
        private const int FirstInfrastructureLocation = 19;
        private const int LastInfrastructureLocation = 39;

        // Returns true if a job was queued (caller should retry), false if nothing could be
        // done (no empty plot available) - caller then falls back to its normal Skip/Stop path.
        private static async ValueTask<bool> HandleAsync(
            Command command,
            GetLayoutBuildingsCommand.Handler getLayoutBuildingsQuery,
            AddJobCommand.Handler addJobCommand,
            ILogger logger,
            IRxQueue rxQueue,
            CancellationToken cancellationToken)
        {
            var (villageId, type, level) = command;

            var buildings = await getLayoutBuildingsQuery.HandleAsync(new(villageId), cancellationToken);

            var plan = GetPrerequisitePlan(buildings, type);
            if (plan is null)
            {
                logger.Warning("Cannot auto-build prerequisite {Type} (level {Level}) in {VillageId} - no empty plot available.", type, level, villageId);
                return false;
            }

            await addJobCommand.HandleAsync(new(villageId, plan.ToJob(), true), cancellationToken);
            rxQueue.Enqueue(new JobsModified(villageId));

            logger.Information("Auto-queued prerequisite {Type} at location {Location} to level {PlanLevel} (target level {Level}) in {VillageId}.", type, plan.Location, plan.Level, level, villageId);

            return true;
        }

        // Pure decision logic, kept separate from the I/O above so it can be unit tested
        // without a database or browser. Bumps an existing instance of `type` up one level,
        // or - if the village has none - claims the first empty infrastructure plot for it.
        // Returns null only when neither option exists.
        public static NormalBuildPlan? GetPrerequisitePlan(List<BuildingItem> buildings, BuildingEnums type)
        {
            var existing = buildings
                .Where(x => x.Type == type)
                .OrderByDescending(x => x.Level)
                .FirstOrDefault();

            if (existing is not null)
            {
                return new NormalBuildPlan()
                {
                    Location = existing.Location,
                    Type = type,
                    Level = existing.Level + 1,
                };
            }

            var site = buildings
                .Where(x => x.Type == BuildingEnums.Site)
                .Where(x => x.Location >= FirstInfrastructureLocation && x.Location <= LastInfrastructureLocation)
                .OrderBy(x => x.Location)
                .FirstOrDefault();

            if (site is null) return null;

            return new NormalBuildPlan()
            {
                Location = site.Location,
                Type = type,
                Level = 1,
            };
        }
    }
}
