namespace MainCore.Commands.Features.UpgradeBuilding
{
    // Reads the village's buildings page (dorf2), and queues a rebuild to level 3 for a missing Main Building, Warehouse or
    // Granary (EssentialBuildingRules). Returns how many jobs were queued. A failure is returned as a failed result and
    // is only ever logged by the caller (a secondary check must never Retry/Stop, CLAUDE.md rule 4).
    [Handler]
    public static partial class EnsureEssentialBuildingsCommand
    {
        public sealed record Command(VillageId VillageId) : IVillageCommand;

        private static async ValueTask<Result<int>> HandleAsync(
            Command command,
            ToDorfCommand.Handler toDorfCommand,
            UpdateBuildingCommand.Handler updateBuildingCommand,
            GetLayoutBuildingsCommand.Handler getLayoutBuildingsCommand,
            AddJobCommand.Handler addJobCommand,
            IRxQueue rxQueue,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            var villageId = command.VillageId;

            var result = await toDorfCommand.HandleAsync(new(2), cancellationToken);
            if (result.IsFailed) return Result.Fail(result.Errors);

            result = await updateBuildingCommand.HandleAsync(new(villageId), cancellationToken);
            if (result.IsFailed) return Result.Fail(result.Errors);

            // Built + under construction + already queued as a job: a rebuild that is already on its way counts as present.
            var buildings = await getLayoutBuildingsCommand.HandleAsync(new(villageId), cancellationToken);

            var plans = EssentialBuildingRules.Plan(buildings);
            foreach (var plan in plans)
            {
                var normalPlan = new NormalBuildPlan() { Location = plan.Location, Type = plan.Type, Level = plan.Level };
                await addJobCommand.HandleAsync(new(villageId, normalPlan.ToJob(), true), cancellationToken);
                logger.Warning(
                    "Village {VillageId} has no {Type} (destroyed?) - queued \"{Plan}\" at the top of the build list.",
                    villageId, plan.Type, normalPlan);
            }

            if (plans.Count > 0) rxQueue.Enqueue(new JobsModified(villageId));
            return Result.Ok(plans.Count);
        }
    }
}
