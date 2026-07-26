using MainCore.Commands.Features.SendResource;
using MainCore.Tasks.Base;

namespace MainCore.Tasks
{
    [Handler]
    public static partial class OverflowToHammerTask
    {
        // Runs on a SIDE village that has OverflowToHammerEnable = true. Sends whichever of
        // its own resources are above the chosen % to the account's configured hammer
        // village, using every free merchant available (split across overflowing resources
        // the same way BalanceResourceTask does).
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

        // Fills the most-overflowing resource's need FIRST (up to its own cap), then moves
        // on to the next one with whatever merchants remain - rather than spreading merchants
        // thin 1-at-a-time across all of them. "resources" is expected most-severe-first.
        private static Dictionary<string, int> DistributeClicks(List<string> resources, int freeMerchants, Dictionary<string, int> maxClicksPerResource)
        {
            var result = resources.ToDictionary(r => r, r => 0);
            var remaining = freeMerchants;

            foreach (var resource in resources)
            {
                if (remaining <= 0) break;

                var cap = maxClicksPerResource.GetValueOrDefault(resource, 0);
                var take = Math.Min(cap, remaining);
                if (take <= 0) continue;

                result[resource] = take;
                remaining -= take;
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

            var hammerStorage = context.Storages.FirstOrDefault(x => x.VillageId == hammerVillage.Id);
            var maxClicksPerResource = new Dictionary<string, int>();
            foreach (var resource in overflowing)
            {
                var room = hammerStorage is null ? long.MaxValue : Math.Max(0, GetCapacity(hammerStorage, resource) - GetAmount(hammerStorage, resource));
                var maxClicks = (int)Math.Min(int.MaxValue, room / capacity);
                if (maxClicks > 0) maxClicksPerResource[resource] = maxClicks;
            }

            if (maxClicksPerResource.Count == 0) return Skip.Error;

            var clicksPerResource = DistributeClicks(overflowing, freeMerchants, maxClicksPerResource);
            if (clicksPerResource.Values.Sum() <= 0) return Skip.Error;

            var sendResult = await sendResourceCommand.HandleAsync(new(task.VillageId, hammerVillageId.Value, clicksPerResource), cancellationToken);
            if (sendResult.IsFailed) return Stop.Error.WithErrors(sendResult.Errors);

            return Result.Ok();
        }
    }
}
