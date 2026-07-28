using MainCore.Commands.Features.SendResource;
using MainCore.Commands.UI.Misc;
using MainCore.Tasks.Base;

namespace MainCore.Tasks
{
    [Handler]
    public static partial class BalanceResourceTask
    {
        // This task runs on a SOURCE village that has AutoBalanceEnable = true.
        // Every time this village's storage is refreshed, we check if wood/clay/iron
        // (against Warehouse capacity) or crop (against Granary capacity) is close to
        // overflowing; if so, we look for another AutoBalanceEnable village of the same
        // account with room for them, and use every free merchant we have (via the "+"
        // buttons, one click = one merchant's worth) split across whichever of those
        // resources are overflowing.
        public sealed class Task : VillageTask
        {
            public Task(AccountId accountId, VillageId villageId) : base(accountId, villageId)
            {
            }

            protected override string TaskName => "Balance resources";

            public override bool CanStart(AppDbContext context)
            {
                var enabled = context.BooleanByName(VillageId, VillageSettingEnums.AutoBalanceEnable);
                if (!enabled) return false;

                var overflowing = GetOverflowingResources(context, VillageId);
                if (overflowing.Count == 0) return false;

                var target = GetBestTarget(context, AccountId, VillageId, overflowing);
                return target is not null;
            }
        }

        private static readonly string[] ResourceTypes = ["wood", "clay", "iron", "crop"];

        private static long GetAmount(Storage storage, string resourceType) => resourceType switch
        {
            "wood" => storage.Wood,
            "clay" => storage.Clay,
            "iron" => storage.Iron,
            "crop" => storage.Crop,
            _ => 0,
        };

        // wood/clay/iron all share the Warehouse capacity, crop uses the Granary.
        private static long GetCapacity(Storage storage, string resourceType) =>
            resourceType == "crop" ? storage.Granary : storage.Warehouse;

        // How much of this resource is spare to send away, i.e. the amount above the
        // "drain down to X%" level - NOT the whole warehouse. This is what stops the bot
        // from emptying the source village completely.
        private static long GetSourceSurplus(Storage storage, string resourceType, int targetPercent)
        {
            var capacity = GetCapacity(storage, resourceType);
            if (capacity <= 0) return 0;

            var downTo = capacity * targetPercent / 100;
            var current = GetAmount(storage, resourceType);
            return Math.Max(0, current - downTo);
        }

        // Resource types currently at/above the overflow threshold, most-full first.
        private static List<string> GetOverflowingResources(AppDbContext context, VillageId villageId)
        {
            var storage = context.Storages.FirstOrDefault(x => x.VillageId == villageId.Value);
            if (storage is null) return [];

            var overflowPercent = context.ByName(villageId, VillageSettingEnums.AutoBalanceOverflowPercent);

            return ResourceTypes
                .Select(r => new { Type = r, Percent = GetCapacity(storage, r) <= 0 ? 0 : GetAmount(storage, r) * 100f / GetCapacity(storage, r) })
                .Where(x => x.Percent >= overflowPercent)
                .OrderByDescending(x => x.Percent)
                .Select(x => x.Type)
                .ToList();
        }

        // The AutoBalanceEnable village (other than the source) with the most combined free
        // room across the resources we're trying to offload.
        private static VillageId? GetBestTarget(AppDbContext context, AccountId accountId, VillageId sourceVillageId, List<string> resources)
        {
            var candidates = context.Villages
                .Where(x => x.AccountId == accountId.Value)
                .Where(x => x.Id != sourceVillageId.Value)
                .Select(x => x.Id)
                .AsEnumerable()
                .Select(id => new VillageId(id))
                .Where(id => context.BooleanByName(id, VillageSettingEnums.AutoBalanceEnable))
                .ToList();

            VillageId? bestId = null;
            long bestRoom = 0;

            foreach (var id in candidates)
            {
                var storage = context.Storages.FirstOrDefault(s => s.VillageId == id.Value);
                if (storage is null) continue;

                var room = resources.Sum(r => Math.Max(0, GetCapacity(storage, r) - GetAmount(storage, r)));
                if (room <= 0) continue;

                if (bestId is null || room > bestRoom)
                {
                    bestId = id;
                    bestRoom = room;
                }
            }

            return bestId;
        }

        // Rounds to the nearest 100 with a small random jitter (+/-3%) first, so shipment
        // amounts don't look like they came out of exact machine math every single time.
        private static long RoundHumanLike(double raw)
        {
            if (raw <= 0) return 0;
            var jitter = 1 + ((Random.Shared.NextDouble() * 0.06) - 0.03);
            var jittered = raw * jitter;
            var rounded = Math.Round(jittered / 100.0) * 100;
            return (long)Math.Max(0, rounded);
        }

        // Splits totalToSend across "resources" proportional to each one's weight (how much
        // surplus it has), capped individually by maxPerResource (target's room, source's
        // surplus). Only resources already in "resources" (i.e. actually overflowing) get
        // anything - everything else is left untouched.
        private static Dictionary<string, long> DistributeProportionally(
            List<string> resources,
            Dictionary<string, long> weights,
            Dictionary<string, long> maxPerResource,
            long totalToSend)
        {
            var result = new Dictionary<string, long>();
            var totalWeight = resources.Sum(r => weights.GetValueOrDefault(r, 0));
            if (totalWeight <= 0 || totalToSend <= 0) return result;

            foreach (var resource in resources)
            {
                var weight = weights.GetValueOrDefault(resource, 0);
                if (weight <= 0) continue;

                var rawShare = (double)totalToSend * weight / totalWeight;
                var cap = maxPerResource.GetValueOrDefault(resource, 0);
                var capped = Math.Min(rawShare, cap);

                var amount = RoundHumanLike(capped);
                if (amount > 0) result[resource] = amount;
            }

            return result;
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
            var overflowing = GetOverflowingResources(context, task.VillageId);
            if (overflowing.Count == 0) return Skip.Error;

            var targetId = GetBestTarget(context, task.AccountId, task.VillageId, overflowing);
            if (targetId is null) return Skip.Error;

            var targetVillage = context.Villages.FirstOrDefault(x => x.Id == targetId.Value.Value);
            if (targetVillage is null) return Skip.Error;

            var pageResult = await toSendResourcePageCommand.HandleAsync(new(task.VillageId), cancellationToken);
            if (pageResult.IsFailed)
            {
                if (pageResult.HasError<MissingBuilding>())
                {
                    var settings = new Dictionary<VillageSettingEnums, int>() {
                        { VillageSettingEnums.AutoBalanceEnable, 0 }
                    };
                    await saveVillageSettingCommand.HandleAsync(new(task.AccountId, task.VillageId, settings), cancellationToken);
                    logger.Warning("No marketplace in this village, disabling auto balance.");
                    return Skip.Error.WithErrors(pageResult.Errors);
                }
                return Stop.Error.WithErrors(pageResult.Errors);
            }

            var freeMerchants = SendResourceParser.GetFreeMerchants(browser.Html);
            if (freeMerchants <= 0)
            {
                // Nothing we can do this cycle - not an error, we'll check again next visit.
                logger.Information("No free merchants in {VillageId}, skipping balance this time.", task.VillageId);
                return Result.Ok();
            }

            var capacity = SendResourceParser.GetMerchantCapacity(browser.Html);
            if (capacity <= 0) capacity = 1;
            var totalToSend = (long)freeMerchants * capacity;

            var targetStorage = context.Storages.FirstOrDefault(x => x.VillageId == targetVillage.Id);
            var sourceStorage = context.Storages.FirstOrDefault(x => x.VillageId == task.VillageId.Value);
            var targetPercent = context.ByName(task.VillageId, VillageSettingEnums.AutoBalanceTargetPercent);

            var weights = new Dictionary<string, long>();
            var maxPerResource = new Dictionary<string, long>();
            foreach (var resource in overflowing)
            {
                var room = targetStorage is null ? long.MaxValue : Math.Max(0, GetCapacity(targetStorage, resource) - GetAmount(targetStorage, resource));
                var surplus = sourceStorage is null ? 0 : GetSourceSurplus(sourceStorage, resource, targetPercent);

                weights[resource] = surplus;
                maxPerResource[resource] = Math.Min(room, surplus);
            }

            var amounts = DistributeProportionally(overflowing, weights, maxPerResource, totalToSend);
            if (amounts.Values.Sum() <= 0) return Skip.Error;

            var sendResult = await sendResourceCommand.HandleAsync(new(task.VillageId, targetId.Value, amounts), cancellationToken);
            if (sendResult.IsFailed) return Stop.Error.WithErrors(sendResult.Errors);

            return Result.Ok();
        }
    }
}
