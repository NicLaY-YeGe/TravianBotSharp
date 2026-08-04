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
                var enabled = context.BooleanByName(VillageId, VillageSettingEnums.SmallCelebrationEnable);
                if (!enabled) return false;

                var busyUntil = context.ByName(VillageId, VillageSettingEnums.SmallCelebrationBusyUntilUnixTime);
                if (busyUntil <= 0) return true;

                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                return now >= busyUntil;
            }
        }

        private static async ValueTask<Result> HandleAsync(
            Task task,
            IChromeBrowser browser,
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

            // If a celebration is already running, remember when it finishes so we don't
            // bother revisiting the Town Hall until then - instead of retrying every cycle.
            var secondsRemaining = TownHallParser.GetOngoingCelebrationSecondsRemaining(browser.Html);
            if (secondsRemaining is not null)
            {
                await SetBusyUntil(task, saveVillageSettingCommand, secondsRemaining.Value, cancellationToken);
                logger.Information("Celebration already running in {VillageId}, {Seconds}s left - won't check again until then.", task.VillageId, secondsRemaining.Value);
                return Result.Ok();
            }

            var result = await holdCelebrationCommand.HandleAsync(new(task.VillageId, HoldCelebrationCommand.SmallCelebration), cancellationToken);
            if (result.IsFailed) return Stop.Error.WithErrors(result.Errors);

            // Safety net: whether we just started a celebration or the page said it isn't
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
                { VillageSettingEnums.SmallCelebrationBusyUntilUnixTime, (int)busyUntil }
            };
            await saveVillageSettingCommand.HandleAsync(new(task.AccountId, task.VillageId, settings), cancellationToken);
        }
    }
}
