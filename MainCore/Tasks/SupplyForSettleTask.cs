using MainCore.Commands.Features.SendResource;
using MainCore.Tasks.Base;

namespace MainCore.Tasks
{
    // Auto Settle's demand-driven mirror of SupplyFromHammerTask (2026-08-12/13, see
    // CLAUDE.md/PROJECT_CONTEXT.md §5k). Runs on a SIBLING village (not the one settling) and
    // sends spare resources - above this village's OWN ExpansionSupplyReservePercent reserve -
    // to top up the target village's settler-training/founding shortfall. Unlike
    // SupplyFromHammerTask (which computes "missing" itself, threshold/push-based), the demand
    // signal here is read directly from the target's own NeedExpansion* settings, written by
    // AutoSettleTask - no opt-in setting on the source side, every other village on the
    // account is a candidate.
    [Handler]
    public static partial class SupplyForSettleTask
    {
        public sealed class Task : VillageTask
        {
            public VillageId TargetVillageId { get; }

            public Task(AccountId accountId, VillageId sourceVillageId, VillageId targetVillageId)
                : base(accountId, sourceVillageId)
            {
                TargetVillageId = targetVillageId;
            }

            protected override string TaskName => "Supply for settle";
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

        public static Dictionary<string, long> GetNeeded(AppDbContext context, VillageId villageId)
        {
            var result = new Dictionary<string, long>();

            var needed = new (string ResourceType, VillageSettingEnums Setting)[]
            {
                ("wood", VillageSettingEnums.NeedExpansionWood),
                ("clay", VillageSettingEnums.NeedExpansionClay),
                ("iron", VillageSettingEnums.NeedExpansionIron),
                ("crop", VillageSettingEnums.NeedExpansionCrop),
            };

            foreach (var (resourceType, setting) in needed)
            {
                var value = context.ByName(villageId, setting);
                if (value > 0) result[resourceType] = value;
            }

            return result;
        }

        // Call whenever a village's NeedExpansion* settings might have changed (AutoSettleTask
        // calls this right after updating them). Queues one delivery task per sibling village
        // that currently has spare of at least one needed resource, above its own reserve.
        public static void RequestIfNeeded(AppDbContext context, AccountId accountId, VillageId villageId, ITaskManager taskManager, ILogger logger)
        {
            var needed = GetNeeded(context, villageId);
            if (needed.Count == 0) return;

            var candidates = context.Villages
                .Where(x => x.AccountId == accountId.Value)
                .Where(x => x.Id != villageId.Value)
                .Select(x => x.Id)
                .AsEnumerable()
                .Select(id => new VillageId(id))
                .ToList();

            foreach (var candidateId in candidates)
            {
                var storage = context.Storages.FirstOrDefault(x => x.VillageId == candidateId.Value);
                if (storage is null) continue;

                var reservePercent = context.ByName(candidateId, VillageSettingEnums.ExpansionSupplyReservePercent);

                var hasSpare = needed.Keys.Any(resourceType =>
                {
                    var capacity = GetCapacity(storage, resourceType);
                    if (capacity <= 0) return false;
                    var reserveLevel = capacity * reservePercent / 100;
                    return GetAmount(storage, resourceType) > reserveLevel;
                });

                if (!hasSpare) continue;

                var task = new Task(accountId, candidateId, villageId);
                if (!taskManager.IsExist<Task>(accountId, candidateId))
                {
                    logger.Information("Requesting village {SourceVillageId} to supply {TargetVillageId} for settling.", candidateId, villageId);
                    taskManager.Add(task);
                }
            }
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
            var needed = GetNeeded(context, task.TargetVillageId);
            if (needed.Count == 0)
            {
                // Target's need was already covered by another source (or its own storage
                // caught up) by the time this task's turn came around.
                return Skip.Error;
            }

            var sourceStorage = context.Storages.FirstOrDefault(x => x.VillageId == task.VillageId.Value);
            if (sourceStorage is null) return Skip.Error;

            var reservePercent = context.ByName(task.VillageId, VillageSettingEnums.ExpansionSupplyReservePercent);

            var pageResult = await toSendResourcePageCommand.HandleAsync(new(task.VillageId), cancellationToken);
            if (pageResult.IsFailed) return Stop.Error.WithErrors(pageResult.Errors);

            var freeMerchants = SendResourceParser.GetFreeMerchants(browser.Html);
            if (freeMerchants <= 0)
            {
                logger.Information("No free merchants in {VillageId}, will retry supplying {Target} later.", task.VillageId, task.TargetVillageId);
                return Result.Ok();
            }

            var capacity = SendResourceParser.GetMerchantCapacity(browser.Html);
            if (capacity <= 0) capacity = 1;
            var totalCapacity = (long)freeMerchants * capacity;

            var amounts = new Dictionary<string, long>();
            foreach (var resourceType in ResourceTypes)
            {
                if (!needed.TryGetValue(resourceType, out var requested) || requested <= 0) continue;

                var reserveLevel = GetCapacity(sourceStorage, resourceType) * reservePercent / 100;
                var spare = Math.Max(0, GetAmount(sourceStorage, resourceType) - reserveLevel);

                var raw = Math.Min(Math.Min(requested, spare), totalCapacity);
                if (raw <= 0) continue;

                var jitter = 1 + ((Random.Shared.NextDouble() * 0.06) - 0.03);
                var amount = (long)Math.Round((raw * jitter) / 100.0) * 100;
                if (amount > 0) amounts[resourceType] = amount;
            }

            if (amounts.Count == 0 || amounts.Values.Sum() == 0)
            {
                logger.Information("{VillageId} has nothing spare to send {Target} right now (reserve protected).", task.VillageId, task.TargetVillageId);
                return Result.Ok();
            }

            var result = await sendResourceCommand.HandleAsync(new(task.VillageId, task.TargetVillageId, amounts), cancellationToken);
            if (result.IsFailed) return Stop.Error.WithErrors(result.Errors);

            return Result.Ok();
        }
    }
}
