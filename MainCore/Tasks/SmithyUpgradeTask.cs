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
                var enabled = context.BooleanByName(VillageId, VillageSettingEnums.SmithyUpgradeEnable);
                if (!enabled) return false;

                var busyUntil = context.ByName(VillageId, VillageSettingEnums.SmithyUpgradeBusyUntilUnixTime);
                if (busyUntil <= 0) return true;

                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                return now >= busyUntil;
            }
        }

        private static async ValueTask<Result> HandleAsync(
            Task task,
            IChromeBrowser browser,
            ToSmithyPageCommand.Handler toSmithyPageCommand,
            SmithyUpgradeCommand.Handler smithyUpgradeCommand,
            SaveVillageSettingCommand.Handler saveVillageSettingCommand,
            AppDbContext context,
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

            // If a research is already running (for this or any other troop type in this
            // smithy - only one can run at a time), remember when it finishes so we don't
            // bother revisiting the Smithy until then.
            var secondsRemaining = SmithyParser.GetOngoingResearchSecondsRemaining(browser.Html);
            if (secondsRemaining is not null)
            {
                var busyUntil = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + secondsRemaining.Value;
                var settings = new Dictionary<VillageSettingEnums, int>() {
                    { VillageSettingEnums.SmithyUpgradeBusyUntilUnixTime, (int)busyUntil }
                };
                await saveVillageSettingCommand.HandleAsync(new(task.AccountId, task.VillageId, settings), cancellationToken);
                logger.Information("Smithy research already running in {VillageId}, {Seconds}s left - won't check again until then.", task.VillageId, secondsRemaining.Value);
                return Result.Ok();
            }

            var result = await smithyUpgradeCommand.HandleAsync(new(task.VillageId, troopSlot), cancellationToken);
            if (result.IsFailed) return Stop.Error.WithErrors(result.Errors);

            return Result.Ok();
        }
    }
}
