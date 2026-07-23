using MainCore.Commands.Features.Demolish;
using MainCore.Commands.Misc;
using MainCore.Commands.UI.Misc;
using MainCore.Models;
using MainCore.Tasks.Base;

namespace MainCore.Tasks
{
    [Handler]
    public static partial class DemolishTask
    {
        // One-shot: demolish the configured building, then queue building the chosen
        // replacement at that same slot, then turn itself off so it doesn't repeat.
        public sealed class Task : VillageTask
        {
            public Task(AccountId accountId, VillageId villageId) : base(accountId, villageId)
            {
            }

            protected override string TaskName => "Demolish";

            public override bool CanStart(AppDbContext context)
            {
                return context.BooleanByName(VillageId, VillageSettingEnums.DemolishEnable);
            }
        }

        private static async ValueTask<Result> HandleAsync(
            Task task,
            AppDbContext context,
            ToDemolishPageCommand.Handler toDemolishPageCommand,
            DemolishCommand.Handler demolishCommand,
            AddJobCommand.Handler addJobCommand,
            SaveVillageSettingCommand.Handler saveVillageSettingCommand,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            var location = context.ByName(task.VillageId, VillageSettingEnums.DemolishSourceLocation);
            var targetType = (BuildingEnums)context.ByName(task.VillageId, VillageSettingEnums.DemolishTargetBuildingType);

            if (location <= 0)
            {
                logger.Warning("Demolish is enabled in {VillageId} but no location is configured.", task.VillageId);
                await Disable(task, saveVillageSettingCommand, cancellationToken);
                return Skip.Error;
            }

            var existing = context.Buildings.FirstOrDefault(x => x.VillageId == task.VillageId.Value && x.Location == location);

            if (existing is not null && existing.Type == targetType)
            {
                // Already demolished and rebuilt - nothing left to do.
                await Disable(task, saveVillageSettingCommand, cancellationToken);
                return Skip.Error;
            }

            if (existing is null)
            {
                // Slot is empty - the old building is gone, queue the replacement.
                var plan = new NormalBuildPlan { Location = location, Level = 1, Type = targetType };
                await addJobCommand.HandleAsync(new(task.VillageId, plan.ToJob()), cancellationToken);
                logger.Information("Queued building {Type} at location {Location} in {VillageId} after demolish.", targetType, location, task.VillageId);

                await Disable(task, saveVillageSettingCommand, cancellationToken);
                return Result.Ok();
            }

            // Old building is still there - demolish it. Demolishing goes through the normal
            // construction queue and takes time, so this may need a few visits before the
            // slot actually becomes empty.
            var pageResult = await toDemolishPageCommand.HandleAsync(new(task.VillageId), cancellationToken);
            if (pageResult.IsFailed)
            {
                if (pageResult.HasError<MissingBuilding>())
                {
                    logger.Warning("No main building in {VillageId}, cannot demolish.", task.VillageId);
                    await Disable(task, saveVillageSettingCommand, cancellationToken);
                    return Skip.Error.WithErrors(pageResult.Errors);
                }
                return Stop.Error.WithErrors(pageResult.Errors);
            }

            var result = await demolishCommand.HandleAsync(new(task.VillageId, location), cancellationToken);
            if (result.IsFailed) return Stop.Error.WithErrors(result.Errors);

            return Result.Ok();
        }

        private static async System.Threading.Tasks.Task Disable(Task task, SaveVillageSettingCommand.Handler saveVillageSettingCommand, CancellationToken cancellationToken)
        {
            var settings = new Dictionary<VillageSettingEnums, int>() {
                { VillageSettingEnums.DemolishEnable, 0 }
            };
            await saveVillageSettingCommand.HandleAsync(new(task.AccountId, task.VillageId, settings), cancellationToken);
        }
    }
}
