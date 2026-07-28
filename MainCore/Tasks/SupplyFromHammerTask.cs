using MainCore.Commands.Features.SendResource;
using MainCore.Tasks.Base;

namespace MainCore.Tasks
{
    [Handler]
    public static partial class SupplyFromHammerTask
    {
        // Runs on the hammer village. Carries the specific target village and the amounts
        // it's short on (snapshotted at the moment the shortfall was detected, with the
        // random 5-10% buffer already applied) - a one-shot delivery, not a recurring policy.
        public sealed class Task : VillageTask
        {
            public VillageId TargetVillageId { get; }
            public Dictionary<string, long> Amounts { get; }

            public Task(AccountId accountId, VillageId hammerVillageId, VillageId targetVillageId, Dictionary<string, long> amounts)
                : base(accountId, hammerVillageId)
            {
                TargetVillageId = targetVillageId;
                Amounts = amounts;
            }

            protected override string TaskName => "Supply from hammer";
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

        private static async ValueTask<Result> HandleAsync(
            Task task,
            AppDbContext context,
            IChromeBrowser browser,
            ToSendResourcePageCommand.Handler toSendResourcePageCommand,
            SendResourceCommand.Handler sendResourceCommand,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            var hammerStorage = context.Storages.FirstOrDefault(x => x.VillageId == task.VillageId.Value);
            if (hammerStorage is null) return Skip.Error;

            var reservePercent = context.ByName(task.AccountId, AccountSettingEnums.HammerReservePercent);

            var pageResult = await toSendResourcePageCommand.HandleAsync(new(task.VillageId), cancellationToken);
            if (pageResult.IsFailed)
            {
                return Stop.Error.WithErrors(pageResult.Errors);
            }

            var freeMerchants = SendResourceParser.GetFreeMerchants(browser.Html);
            if (freeMerchants <= 0)
            {
                logger.Information("No free merchants in hammer village {VillageId}, will retry supplying {Target} later.", task.VillageId, task.TargetVillageId);
                return Result.Ok();
            }

            var capacity = SendResourceParser.GetMerchantCapacity(browser.Html);
            if (capacity <= 0) capacity = 1;

            var totalCapacity = (long)freeMerchants * capacity;
            var amounts = new Dictionary<string, long>();
            foreach (var resourceType in ResourceTypes)
            {
                if (!task.Amounts.TryGetValue(resourceType, out var requested) || requested <= 0) continue;

                // Never dip below the hammer village's own reserve for that resource.
                var reserveLevel = GetCapacity(hammerStorage, resourceType) * reservePercent / 100;
                var spare = Math.Max(0, GetAmount(hammerStorage, resourceType) - reserveLevel);

                var raw = Math.Min(Math.Min(requested, spare), totalCapacity);
                if (raw <= 0) continue;

                var jitter = 1 + ((Random.Shared.NextDouble() * 0.06) - 0.03);
                var amount = (long)Math.Round((raw * jitter) / 100.0) * 100;
                if (amount > 0) amounts[resourceType] = amount;
            }

            if (amounts.Count == 0 || amounts.Values.Sum() == 0)
            {
                logger.Information("Hammer village {VillageId} has nothing spare to send {Target} right now (reserve protected).", task.VillageId, task.TargetVillageId);
                return Result.Ok();
            }

            var result = await sendResourceCommand.HandleAsync(new(task.VillageId, task.TargetVillageId, amounts), cancellationToken);
            if (result.IsFailed) return Stop.Error.WithErrors(result.Errors);

            return Result.Ok();
        }
    }
}
