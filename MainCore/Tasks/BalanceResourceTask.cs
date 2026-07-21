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

        // Give each resource type one merchant at a time, round-robin, until either the
        // merchants run out or every resource has hit the most it's useful to send (limited
        // by how much room the target actually has for it).
        private static Dictionary<string, int> DistributeClicks(List<string> resources, int freeMerchants, Dictionary<string, int> maxClicksPerResource)
        {
            var result = resources.ToDictionary(r => r, r => 0);
            var remaining = freeMerchants;

            while (remaining > 0)
            {
                var progressed = false;
                foreach (var resource in resources)
                {
                    if (remaining <= 0) break;
                    if (result[resource] < maxClicksPerResource.GetValueOrDefault(resource, 0))
                    {
                        result[resource]++;
                        remaining--;
                        progressed = true;
                    }
                }
                if (!progressed) break;
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

            var targetStorage = context.Storages.FirstOrDefault(x => x.VillageId == targetVillage.Id);
            var sourceStorage = context.Storages.FirstOrDefault(x => x.VillageId == task.VillageId.Value);
            var targetPercent = context.ByName(task.VillageId, VillageSettingEnums.AutoBalanceTargetPercent);

            var maxClicksPerResource = new Dictionary<string, int>();
            foreach (var resource in overflowing)
            {
                var room = targetStorage is null ? long.MaxValue : Math.Max(0, GetCapacity(targetStorage, resource) - GetAmount(targetStorage, resource));
                var surplus = sourceStorage is null ? 0 : GetSourceSurplus(sourceStorage, resource, targetPercent);

                var maxClicks = (int)Math.Min(room / capacity, surplus / capacity);
                if (maxClicks > 0) maxClicksPerResource[resource] = maxClicks;
            }

            if (maxClicksPerResource.Count == 0) return Skip.Error;

            var clicksPerResource = DistributeClicks(overflowing, freeMerchants, maxClicksPerResource);
            if (clicksPerResource.Values.Sum() <= 0) return Skip.Error;

            var sendResult = await sendResourceCommand.HandleAsync(new(task.VillageId, targetId.Value, clicksPerResource), cancellationToken);
            if (sendResult.IsFailed) return Stop.Error.WithErrors(sendResult.Errors);

            return Result.Ok();
        }
    }
}
