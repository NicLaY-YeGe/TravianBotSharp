using MainCore.Commands.Features.SendResource;
using MainCore.Tasks.Base;

namespace MainCore.Tasks
{
    [Handler]
    public static partial class SupplyFromHammerTask
    {
        // Runs on the hammer village. Tops up the target (needy) village's warehouse AND
        // granary up to its configured % using hammer village supply. Amounts are computed
        // fresh at execution time (not snapshotted), so this stays correct even if the queue
        // was delayed or another source already helped in the meantime.
        public sealed class Task : VillageTask
        {
            public VillageId TargetVillageId { get; }

            public Task(AccountId accountId, VillageId hammerVillageId, VillageId targetVillageId)
                : base(accountId, hammerVillageId)
            {
                TargetVillageId = targetVillageId;
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

        // How much this village is short of the "fill up to target %" goal, per resource.
        public static Dictionary<string, long> GetMissingAmounts(AppDbContext context, VillageId villageId)
        {
            var result = new Dictionary<string, long>();

            var storage = context.Storages.FirstOrDefault(x => x.VillageId == villageId.Value);
            if (storage is null) return result;

            var targetPercent = context.ByName(villageId, VillageSettingEnums.SupplyFromHammerTargetPercent);

            foreach (var resourceType in ResourceTypes)
            {
                var capacity = GetCapacity(storage, resourceType);
                if (capacity <= 0) continue;

                var targetLevel = capacity * targetPercent / 100;
                var current = GetAmount(storage, resourceType);
                var missing = targetLevel - current;
                if (missing > 0) result[resourceType] = missing;
            }

            return result;
        }

        // Finds the account's configured hammer village, if any (and it isn't the village
        // asking for help).
        public static VillageId? GetHammerVillageId(AppDbContext context, AccountId accountId, VillageId requestingVillageId)
        {
            var hammerVillageIdRaw = context.ByName(accountId, AccountSettingEnums.HammerVillageId);
            if (hammerVillageIdRaw <= 0 || hammerVillageIdRaw == requestingVillageId.Value) return null;

            var hammerVillage = context.Villages.FirstOrDefault(x => x.Id == hammerVillageIdRaw && x.AccountId == accountId.Value);
            return hammerVillage is null ? null : new VillageId(hammerVillage.Id);
        }

        // Shared entry point: call this whenever a village might need topping up (periodic
        // storage check, or right after a build job fails from lack of resources). Queues
        // one-shot delivery tasks - the hammer village first, and also any other
        // AutoBalance-opted village that has spare of a resource the hammer might not be
        // able to fully cover (each source independently re-checks what's still actually
        // needed once it's their turn, so there's no double-sending).
        public static void RequestIfNeeded(AppDbContext context, AccountId accountId, VillageId villageId, ITaskManager taskManager, ILogger logger)
        {
            var enabled = context.BooleanByName(villageId, VillageSettingEnums.SupplyFromHammerEnable);
            if (!enabled) return;

            var missing = GetMissingAmounts(context, villageId);
            if (missing.Count == 0) return;

            var sources = new List<VillageId>();

            var hammerVillageId = GetHammerVillageId(context, accountId, villageId);
            if (hammerVillageId is not null) sources.Add(hammerVillageId.Value);

            sources.AddRange(GetFallbackSources(context, accountId, villageId, missing, hammerVillageId));

            foreach (var sourceVillageId in sources)
            {
                var task = new Task(accountId, sourceVillageId, villageId);
                if (!taskManager.IsExist<Task>(accountId, sourceVillageId))
                {
                    logger.Information("Requesting village {SourceVillageId} to supply village {VillageId}.", sourceVillageId, villageId);
                    taskManager.Add(task);
                }
            }
        }

        // Other AutoBalance-opted villages (not the hammer, not the needy village itself)
        // that currently have some spare of at least one of the missing resources, above
        // their own "drain down to X%" level. These act as a fallback so a resource the
        // hammer can't fully cover (because of ITS OWN reserve) still gets delivered from
        // somewhere.
        private static List<VillageId> GetFallbackSources(
            AppDbContext context,
            AccountId accountId,
            VillageId needyVillageId,
            Dictionary<string, long> missing,
            VillageId? hammerVillageId)
        {
            var result = new List<VillageId>();

            var candidates = context.Villages
                .Where(x => x.AccountId == accountId.Value)
                .Where(x => x.Id != needyVillageId.Value)
                .Where(x => hammerVillageId is null || x.Id != hammerVillageId.Value.Value)
                .Select(x => x.Id)
                .AsEnumerable()
                .Select(id => new VillageId(id))
                .Where(id => context.BooleanByName(id, VillageSettingEnums.AutoBalanceEnable))
                .ToList();

            foreach (var candidateId in candidates)
            {
                var storage = context.Storages.FirstOrDefault(x => x.VillageId == candidateId.Value);
                if (storage is null) continue;

                var targetPercent = context.ByName(candidateId, VillageSettingEnums.AutoBalanceTargetPercent);

                var hasSpare = missing.Keys.Any(resourceType =>
                {
                    var capacity = GetCapacity(storage, resourceType);
                    if (capacity <= 0) return false;
                    var reserveLevel = capacity * targetPercent / 100;
                    return GetAmount(storage, resourceType) > reserveLevel;
                });

                if (hasSpare) result.Add(candidateId);
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
            var missing = GetMissingAmounts(context, task.TargetVillageId);
            if (missing.Count == 0)
            {
                // Target recovered already (another source helped, or it produced enough
                // on its own) - nothing left to do.
                return Skip.Error;
            }

            var sourceStorage = context.Storages.FirstOrDefault(x => x.VillageId == task.VillageId.Value);
            if (sourceStorage is null) return Skip.Error;

            // The reserve level depends on whether this source is the hammer village itself
            // or a fallback (AutoBalance-opted) village helping to cover what the hammer
            // couldn't - each uses its own configured "don't go below" setting.
            var hammerVillageIdRaw = context.ByName(task.AccountId, AccountSettingEnums.HammerVillageId);
            var reservePercent = hammerVillageIdRaw == task.VillageId.Value
                ? context.ByName(task.AccountId, AccountSettingEnums.HammerReservePercent)
                : context.ByName(task.VillageId, VillageSettingEnums.AutoBalanceTargetPercent);

            var pageResult = await toSendResourcePageCommand.HandleAsync(new(task.VillageId), cancellationToken);
            if (pageResult.IsFailed)
            {
                return Stop.Error.WithErrors(pageResult.Errors);
            }

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
                if (!missing.TryGetValue(resourceType, out var requested) || requested <= 0) continue;

                // Never dip below this source's own reserve for that resource.
                var reserveLevel = GetCapacity(sourceStorage, resourceType) * reservePercent / 100;
                var spare = Math.Max(0, GetAmount(sourceStorage, resourceType) - reserveLevel);

                var raw = Math.Min(Math.Min(requested, spare), totalCapacity);
                if (raw <= 0) continue;

                // Small human-like jitter, rounded to the nearest 100.
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
