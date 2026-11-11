using MainCore.Commands.Features.BuyCrop;
using MainCore.Commands.UI.Misc;
using MainCore.Tasks.Base;

namespace MainCore.Tasks
{
    // 2026-11-10 (user request, purpose: keep the troops from starving, even temporarily): buys crop on the Marketplace
    // "Buy" tab when the granary runs low. Independent of the NPC exchange (NpcTask) and of the construction reserve.
    //
    // Lifecycle (like SmithyUpgradeTask, not bootstrapped in RxQueue on purpose): UpdateStorageCommand and
    // SaveVillageSettingCommand queue it whenever CanStart passes. The task ends with ExecuteAt unchanged, so
    // TimerManager removes it (CLAUDE.md rule 16 - intended here); the "not again before" time lives in BuyCropMemory
    // and CanStart reads it, so an empty offer list does not turn into a Marketplace visit every minute.
    //
    // Secondary task (CLAUDE.md rule 4): hard time budget, no failure is ever Retry or Stop. A user pause stays a
    // pause, a dead browser is the behaviors' business, everything else becomes "look again later".
    [Handler]
    public static partial class BuyCropTask
    {
        public sealed class Task : VillageTask
        {
            public Task(AccountId accountId, VillageId villageId) : base(accountId, villageId)
            {
            }

            protected override string TaskName => "Buy crop";

            public override bool CanStart(AppDbContext context)
            {
                if (!context.BooleanByName(VillageId, VillageSettingEnums.BuyCropEnable)) return false;
                if (!BuyCropMemory.IsDue(VillageId.Value)) return false;

                var storage = context.Storages
                    .Where(x => x.VillageId == VillageId.Value)
                    .Select(x => new { x.Crop, x.Granary })
                    .FirstOrDefault();
                if (storage is null) return false;

                var threshold = context.ByName(VillageId, VillageSettingEnums.BuyCropGranaryPercent);
                return BuyCropRules.ShouldBuy(storage.Crop, BuyCropMemory.PendingAmount(VillageId.Value), storage.Granary, threshold);
            }
        }

        private const int BudgetSeconds = 120;

        private static async ValueTask<Result> HandleAsync(
            Task task,
            IChromeBrowser browser,
            ToMarketplaceBuyPageCommand.Handler toBuyPageCommand,
            BuyCropCommand.Handler buyCropCommand,
            SaveVillageSettingCommand.Handler saveVillageSettingCommand,
            AppDbContext context,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(BudgetSeconds));

            var result = await RunAsync(task, toBuyPageCommand, buyCropCommand, saveVillageSettingCommand, context, logger, deadline.Token);
            if (result.IsSuccess) return result;

            // A user pause stays a pause; a dead browser is the behaviors' business; "no Marketplace" already disabled the setting.
            if (cancellationToken.IsCancellationRequested) return result;
            if (result.HasError<BrowserClosed>() || result.HasError<MissingBuilding>()) return result;

            var timedOut = deadline.IsCancellationRequested;
            if (timedOut) await PageDump.SaveAsync(browser, logger, "buycrop-timeout");

            var reasons = string.Join(" | ", result.Reasons.Select(x => x.Message).Where(x => !string.IsNullOrWhiteSpace(x)));
            if (timedOut) reasons = $"no answer within {BudgetSeconds}s" + (reasons.Length > 0 ? $" ({reasons})" : "");

            var next = BuyCropRules.NextCheck(DateTime.Now, BuyCropRules.FailedMinMinutes, BuyCropRules.FailedMaxMinutes, Random.Shared);
            BuyCropMemory.SetNextCheck(task.VillageId.Value, next);
            logger.Warning("Buy crop in {VillageId}: could not finish ({Reasons}) - next look at {Next:HH:mm:ss}.", task.VillageId, reasons, next);
            return Result.Ok();
        }

        // The token carries the user's pause plus the time budget (every browser call gets it).
        private static async ValueTask<Result> RunAsync(
            Task task,
            ToMarketplaceBuyPageCommand.Handler toBuyPageCommand,
            BuyCropCommand.Handler buyCropCommand,
            SaveVillageSettingCommand.Handler saveVillageSettingCommand,
            AppDbContext context,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            var id = task.VillageId.Value;

            // Re-decide against the live numbers: the task may have waited in the queue while the situation changed.
            var storage = context.Storages.FirstOrDefault(x => x.VillageId == id);
            var threshold = context.ByName(task.VillageId, VillageSettingEnums.BuyCropGranaryPercent);
            if (storage is null || !BuyCropRules.ShouldBuy(storage.Crop, BuyCropMemory.PendingAmount(id), storage.Granary, threshold))
            {
                var next = BuyCropRules.NextCheck(DateTime.Now, BuyCropRules.NothingBoughtMinMinutes, BuyCropRules.NothingBoughtMaxMinutes, Random.Shared);
                BuyCropMemory.SetNextCheck(id, next);
                logger.Information("Buy crop in {VillageId}: the granary is not below {Threshold}% any more - nothing to buy.", task.VillageId, threshold);
                return Result.Ok();
            }

            var pageResult = await toBuyPageCommand.HandleAsync(new(task.VillageId), cancellationToken);
            if (pageResult.IsFailed)
            {
                if (pageResult.HasError<MissingBuilding>())
                {
                    var settings = new Dictionary<VillageSettingEnums, int>() {
                        { VillageSettingEnums.BuyCropEnable, 0 }
                    };
                    await saveVillageSettingCommand.HandleAsync(new(task.AccountId, task.VillageId, settings), cancellationToken);
                    logger.Warning("No Marketplace in this village, disabling buy crop.");
                }
                return Skip.Error.WithErrors(pageResult.Errors);
            }

            return await buyCropCommand.HandleAsync(new(task.VillageId), cancellationToken);
        }
    }
}
