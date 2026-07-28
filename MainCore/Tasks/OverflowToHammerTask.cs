using MainCore.Commands.Features.SendResource;
using MainCore.Tasks.Base;

namespace MainCore.Tasks
{
    [Handler]
    public static partial class OverflowToHammerTask
    {
        // Runs on a SIDE village that has OverflowToHammerEnable = true. Sends whichever of
        // its own resources are above the chosen % to the account's configured hammer
        // village, splitting every free merchant's capacity proportionally across however
        // many resources are overflowing (weighted by how much each one is overflowing by).
        public sealed class Task : VillageTask
        {
            public Task(AccountId accountId, VillageId villageId) : base(accountId, villageId)
            {
            }

            protected override string TaskName => "Overflow to hammer";

            public override bool CanStart(AppDbContext context)
            {
                var enabled = context.BooleanByName(VillageId, VillageSettingEnums.OverflowToHammerEnable);
                if (!enabled) return false;

                var hammerVillageId = GetHammerVillageId(context, AccountId, VillageId);
                if (hammerVillageId is null) return false;

                return GetOverflowingResources(context, VillageId).Count > 0;
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

        private static long GetCapacity(Storage storage, string resourceType) =>
            resourceType == "crop" ? storage.Granary : storage.Warehouse;

        private static VillageId? GetHammerVillageId(AppDbContext context, AccountId accountId, VillageId sourceVillageId)
        {
            var hammerVillageIdRaw = context.ByName(accountId, AccountSettingEnums.HammerVillageId);
            if (hammerVillageIdRaw <= 0 || hammerVillageIdRaw == sourceVillageId.Value) return null;

            var hammerVillage = context.Villages.FirstOrDefault(x => x.Id == hammerVillageIdRaw && x.AccountId == accountId.Value);
            return hammerVillage is null ? null : new VillageId(hammerVillage.Id);
        }

        // Resource types currently at/above the chosen overflow threshold, most-full first.
        private static List<string> GetOverflowingResources(AppDbContext context, VillageId villageId)
        {
            var storage = context.Storages.FirstOrDefault(x => x.VillageId == villageId.Value);
            if (storage is null) return [];

            var overflowPercent = context.ByName(villageId, VillageSettingEnums.OverflowToHammerPercent);

            return ResourceTypes
                .Select(r => new { Type = r, Percent = GetCapacity(storage, r) <= 0 ? 0 : GetAmount(storage, r) * 100f / GetCapacity(storage, r) })
                .Where(x => x.Percent >= overflowPercent)
                .OrderByDescending(x => x.Percent)
                .Select(x => x.Type)
                .ToList();
        }

        // How much of this resource sits above the overflow threshold - this is both the
        // weight used to split merchants across resources, and the hard cap on how much of
        // it makes sense to send (never more than what's actually "extra").
        private static long GetOverflowAmount(Storage storage, string resourceType, int overflowPercent)
        {
            var capacity = GetCapacity(storage, resourceType);
            if (capacity <= 0) return 0;

            var thresholdAmount = capacity * overflowPercent / 100;
            var current = GetAmount(storage, resourceType);
            return Math.Max(0, current - thresholdAmount);
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
        // it's overflowing by), capped individually by maxPerResource (hammer's room, this
        // village's own overflow amount). Only overflowing resources get anything.
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
            ILogger logger,
            CancellationToken cancellationToken)
        {
            var overflowing = GetOverflowingResources(context, task.VillageId);
            if (overflowing.Count == 0) return Skip.Error;

            var hammerVillageId = GetHammerVillageId(context, task.AccountId, task.VillageId);
            if (hammerVillageId is null) return Skip.Error;

            var hammerVillage = context.Villages.FirstOrDefault(x => x.Id == hammerVillageId.Value.Value);
            if (hammerVillage is null) return Skip.Error;

            var pageResult = await toSendResourcePageCommand.HandleAsync(new(task.VillageId), cancellationToken);
            if (pageResult.IsFailed)
            {
                if (pageResult.HasError<MissingBuilding>())
                {
                    logger.Warning("No marketplace in {VillageId}, cannot send overflow to hammer.", task.VillageId);
                    return Skip.Error.WithErrors(pageResult.Errors);
                }
                return Stop.Error.WithErrors(pageResult.Errors);
            }

            var freeMerchants = SendResourceParser.GetFreeMerchants(browser.Html);
            if (freeMerchants <= 0)
            {
                logger.Information("No free merchants in {VillageId}, skipping overflow-to-hammer this time.", task.VillageId);
                return Result.Ok();
            }

            var capacity = SendResourceParser.GetMerchantCapacity(browser.Html);
            if (capacity <= 0) capacity = 1;
            var totalToSend = (long)freeMerchants * capacity;

            var overflowPercent = context.ByName(task.VillageId, VillageSettingEnums.OverflowToHammerPercent);
            var sourceStorage = context.Storages.FirstOrDefault(x => x.VillageId == task.VillageId.Value);
            var hammerStorage = context.Storages.FirstOrDefault(x => x.VillageId == hammerVillage.Id);

            var weights = new Dictionary<string, long>();
            var maxPerResource = new Dictionary<string, long>();
            foreach (var resource in overflowing)
            {
                var room = hammerStorage is null ? long.MaxValue : Math.Max(0, GetCapacity(hammerStorage, resource) - GetAmount(hammerStorage, resource));
                var overflowAmount = sourceStorage is null ? 0 : GetOverflowAmount(sourceStorage, resource, overflowPercent);

                weights[resource] = overflowAmount;
                maxPerResource[resource] = Math.Min(room, overflowAmount);
            }

            var amounts = DistributeProportionally(overflowing, weights, maxPerResource, totalToSend);
            if (amounts.Values.Sum() <= 0) return Skip.Error;

            var sendResult = await sendResourceCommand.HandleAsync(new(task.VillageId, hammerVillageId.Value, amounts), cancellationToken);
            if (sendResult.IsFailed) return Stop.Error.WithErrors(sendResult.Errors);

            return Result.Ok();
        }
    }
}
