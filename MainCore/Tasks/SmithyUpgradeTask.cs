using MainCore.Commands.Features.SmithyUpgrade;
using MainCore.Commands.UI.Misc;
using MainCore.Tasks.Base;

namespace MainCore.Tasks
{
    [Handler]
    public static partial class SmithyUpgradeTask
    {
        public sealed class Task : VillageTask
        {
            public Task(AccountId accountId, VillageId villageId) : base(accountId, villageId)
            {
            }

            protected override string TaskName => "Smithy upgrade";

            public override bool CanStart(AppDbContext context)
            {
                return context.BooleanByName(VillageId, VillageSettingEnums.SmithyUpgradeEnable);
            }
        }

        private static async ValueTask<Result> HandleAsync(
            Task task,
            AppDbContext context,
            ToSmithyPageCommand.Handler toSmithyPageCommand,
            SmithyUpgradeCommand.Handler smithyUpgradeCommand,
            SaveVillageSettingCommand.Handler saveVillageSettingCommand,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            var troopSlot = context.ByName(task.VillageId, VillageSettingEnums.SmithyUpgradeTroopSlot);
            if (troopSlot <= 0) troopSlot = 1;

            var pageResult = await toSmithyPageCommand.HandleAsync(new(task.VillageId), cancellationToken);
            if (pageResult.IsFailed)
            {
                if (pageResult.HasError<MissingBuilding>())
                {
                    var settings = new Dictionary<VillageSettingEnums, int>() {
                        { VillageSettingEnums.SmithyUpgradeEnable, 0 }
                    };
                    await saveVillageSettingCommand.HandleAsync(new(task.AccountId, task.VillageId, settings), cancellationToken);
                    logger.Warning("No smithy in this village, disabling smithy upgrade.");
                    return Skip.Error.WithErrors(pageResult.Errors);
                }
                return Stop.Error.WithErrors(pageResult.Errors);
            }

            var result = await smithyUpgradeCommand.HandleAsync(new(task.VillageId, troopSlot), cancellationToken);
            if (result.IsFailed) return Stop.Error.WithErrors(result.Errors);

            return Result.Ok();
        }
    }
}
