using MainCore.Commands.Features.SendResource;
using MainCore.Commands.UI.Misc;
using MainCore.Tasks.Base;

namespace MainCore.Tasks
{
    [Handler]
    public static partial class SendCropTask
    {
        // This task runs on a SOURCE village (a village that has AutoSendCropSourceEnable = true).
        // Every time this village's storage is refreshed, we check whether any OTHER village of
        // the same account has AutoSendCropEnable = true and its granary is below the configured
        // threshold; if so, and this village has spare crop, we send merchants over.
        public sealed class Task : VillageTask
        {
            public Task(AccountId accountId, VillageId villageId) : base(accountId, villageId)
            {
            }

            protected override string TaskName => "Send crop";

            public override bool CanStart(AppDbContext context)
            {
                var sourceEnabled = context.BooleanByName(VillageId, VillageSettingEnums.AutoSendCropSourceEnable);
                if (!sourceEnabled) return false;

                var plan = GetPlan(context, AccountId, VillageId);
                return plan is not null;
            }
        }

        public sealed record Plan(VillageId TargetVillageId, long Amount);

        // Shared by CanStart and HandleAsync so the decision is always re-evaluated against
        // current data (queue delays, other tasks finishing first, etc. can change the numbers).
        public static Plan? GetPlan(AppDbContext context, AccountId accountId, VillageId sourceVillageId)
        {
            var sourceStorage = context.Storages
                .FirstOrDefault(x => x.VillageId == sourceVillageId.Value);
            if (sourceStorage is null) return null;

            var reservePercent = context.ByName(sourceVillageId, VillageSettingEnums.AutoSendCropReservePercent);
            var reserveAmount = sourceStorage.Granary * reservePercent / 100;
            var spareCrop = sourceStorage.Crop - reserveAmount;
            if (sourceStorage.FreeCrop >= 0)
            {
                spareCrop = Math.Min(spareCrop, sourceStorage.FreeCrop);
            }
            if (spareCrop <= 0) return null;

            var otherVillageIds = context.Villages
                .Where(x => x.AccountId == accountId.Value)
                .Where(x => x.Id != sourceVillageId.Value)
                .Select(x => x.Id)
                .AsEnumerable()
                .Select(id => new VillageId(id))
                .Where(id => context.BooleanByName(id, VillageSettingEnums.AutoSendCropEnable))
                .ToList();

            VillageId? bestTargetId = null;
            long bestMissing = 0;
            float bestPercent = float.MaxValue;

            foreach (var targetId in otherVillageIds)
            {
                var storage = context.Storages.FirstOrDefault(s => s.VillageId == targetId.Value);
                if (storage is null) continue;
                if (storage.Granary <= 0) continue;

                var threshold = context.ByName(targetId, VillageSettingEnums.AutoSendCropGranaryPercent);
                var percent = storage.Crop * 100f / storage.Granary;
                if (percent >= threshold) continue;

                var missing = (storage.Granary * threshold / 100) - storage.Crop;
                if (missing <= 0) continue;

                if (bestTargetId is null || percent < bestPercent)
                {
                    bestTargetId = targetId;
                    bestPercent = percent;
                    bestMissing = missing;
                }
            }

            if (bestTargetId is null) return null;

            var amount = Math.Min(spareCrop, bestMissing);
            if (amount <= 0) return null;

            return new Plan(bestTargetId.Value, amount);
        }

        private static async ValueTask<Result> HandleAsync(
            Task task,
            AppDbContext context,
            IChromeBrowser browser,
            ToSendResourcePageCommand.Handler toSendResourcePageCommand,
            SendResourceCommand.Handler sendResourceCommand,
            SaveVillageSettingCommand.Handler saveVillageSettingCommand,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            var plan = GetPlan(context, task.AccountId, task.VillageId);
            if (plan is null)
            {
                // Nothing to do anymore (target recovered, or another village already covered it).
                return Skip.Error;
            }

            var pageResult = await toSendResourcePageCommand.HandleAsync(new(task.VillageId), cancellationToken);
            if (pageResult.IsFailed)
            {
                if (pageResult.HasError<MissingBuilding>())
                {
                    var settings = new Dictionary<VillageSettingEnums, int>() {
                        { VillageSettingEnums.AutoSendCropSourceEnable, 0 }
                    };
                    await saveVillageSettingCommand.HandleAsync(new(task.AccountId, task.VillageId, settings), cancellationToken);
                    logger.Warning("No marketplace in this village, disabling it as a crop source.");
                    return Skip.Error.WithErrors(pageResult.Errors);
                }
                return Stop.Error.WithErrors(pageResult.Errors);
            }

            var freeMerchants = SendResourceParser.GetFreeMerchants(browser.Html);
            if (freeMerchants <= 0)
            {
                // Nothing we can do this cycle - not an error, we'll check again next visit.
                logger.Information("No free merchants in {VillageId}, skipping crop send this time.", task.VillageId);
                return Result.Ok();
            }

            var capacity = SendResourceParser.GetMerchantCapacity(browser.Html);
            if (capacity <= 0) capacity = 1;

            // Use every free merchant we have for crop, capped by how much is actually
            // spare/needed. Even if the deficit is smaller than one merchant's capacity,
            // still send one - a partial top-up is better than never topping up at all.
            var neededClicks = (int)((plan.Amount + capacity - 1) / capacity); // round up
            var maxUsefulClicks = Math.Max(1, Math.Min(freeMerchants, neededClicks));

            logger.Information(
                "Village {VillageId}: {SpareOrNeeded} crop to move, {Capacity} per merchant, {FreeMerchants} free -> sending {Clicks} merchant(s).",
                task.VillageId, plan.Amount, capacity, freeMerchants, maxUsefulClicks);

            var clicksPerResource = new Dictionary<string, int> { { "crop", maxUsefulClicks } };

            var sendResult = await sendResourceCommand.HandleAsync(new(task.VillageId, plan.TargetVillageId, clicksPerResource), cancellationToken);
            if (sendResult.IsFailed) return Stop.Error.WithErrors(sendResult.Errors);

            return Result.Ok();
        }
    }
}
