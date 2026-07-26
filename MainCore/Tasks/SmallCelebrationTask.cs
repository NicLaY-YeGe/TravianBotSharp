using MainCore.Commands.Features.Celebration;
using MainCore.Commands.UI.Misc;
using MainCore.Tasks.Base;

namespace MainCore.Tasks
{
    [Handler]
    public static partial class SmallCelebrationTask
    {
        public sealed class Task : VillageTask
        {
            public Task(AccountId accountId, VillageId villageId) : base(accountId, villageId)
            {
            }

            protected override string TaskName => "Small celebration";

            public override bool CanStart(AppDbContext context)
            {
                return context.BooleanByName(VillageId, VillageSettingEnums.SmallCelebrationEnable);
            }
        }

        private static async ValueTask<Result> HandleAsync(
            Task task,
            ToTownHallPageCommand.Handler toTownHallPageCommand,
            HoldCelebrationCommand.Handler holdCelebrationCommand,
            SaveVillageSettingCommand.Handler saveVillageSettingCommand,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            var pageResult = await toTownHallPageCommand.HandleAsync(new(task.VillageId), cancellationToken);
            if (pageResult.IsFailed)
            {
                if (pageResult.HasError<MissingBuilding>())
                {
                    var settings = new Dictionary<VillageSettingEnums, int>() {
                        { VillageSettingEnums.SmallCelebrationEnable, 0 }
                    };
                    await saveVillageSettingCommand.HandleAsync(new(task.AccountId, task.VillageId, settings), cancellationToken);
                    logger.Warning("No town hall in this village, disabling small celebration.");
                    return Skip.Error.WithErrors(pageResult.Errors);
                }
                return Stop.Error.WithErrors(pageResult.Errors);
            }

            var result = await holdCelebrationCommand.HandleAsync(new(task.VillageId, HoldCelebrationCommand.SmallCelebration), cancellationToken);
            if (result.IsFailed) return Stop.Error.WithErrors(result.Errors);

            return Result.Ok();
        }
    }
}
