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

            // NOTE: AccountsInfo.Tribe is NOT a reliable source here - UpdateAccountInfoCommand
            // hard-codes it to TribeEnums.Any and never actually parses it from the page, so it's
            // effectively always "Any" (see CHANGELOG.md, 2026-08-09 - this caused the tribe-aware
            // GetResearchBlock fix to still fail with the same "research block not found" error,
            // since RallyPointTroopSlots.GetSlots(Any) returns an empty list). The account's real
            // tribe lives in the AccountSettingEnums.Tribe key-value setting instead - the same
            // one set via the Tribe selector on the Account Setting tab.
            var tribe = (TribeEnums)context.ByName(task.AccountId, AccountSettingEnums.Tribe);

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
                await SetBusyUntil(task, saveVillageSettingCommand, secondsRemaining.Value, cancellationToken);
                logger.Information("Smithy research already running in {VillageId}, {Seconds}s left - won't check again until then.", task.VillageId, secondsRemaining.Value);
                return Result.Ok();
            }

            var result = await smithyUpgradeCommand.HandleAsync(new(task.VillageId, troopSlot, tribe), cancellationToken);
            if (result.IsFailed) return Stop.Error.WithErrors(result.Errors);

            // Safety net: whether we just started a research or the page said it isn't
            // available for some reason we didn't specifically detect, don't come back to
            // this page again for a while. Repeatedly hammering the same page every cycle
            // is exactly the kind of pattern that looks bad, even if each visit is harmless.
            const int minCooldownSeconds = 20 * 60;
            await SetBusyUntil(task, saveVillageSettingCommand, minCooldownSeconds, cancellationToken);

            return Result.Ok();
        }

        private static async System.Threading.Tasks.Task SetBusyUntil(Task task, SaveVillageSettingCommand.Handler saveVillageSettingCommand, int secondsFromNow, CancellationToken cancellationToken)
        {
            var busyUntil = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + secondsFromNow;
            var settings = new Dictionary<VillageSettingEnums, int>() {
                { VillageSettingEnums.SmithyUpgradeBusyUntilUnixTime, (int)busyUntil }
            };
            await saveVillageSettingCommand.HandleAsync(new(task.AccountId, task.VillageId, settings), cancellationToken);
        }
    }
}
