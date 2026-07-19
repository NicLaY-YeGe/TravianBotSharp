using MainCore.Commands.Features.SendResource;
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
        // account with room for that resource and send the surplus over with merchants.
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

                var plan = GetPlan(context, AccountId, VillageId);
                return plan is not null;
            }
        }

        public sealed record Plan(string ResourceType, VillageId TargetVillageId, long Amount);

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

        // Shared by CanStart and HandleAsync so the decision is always re-evaluated against
        // current data (queue delays, other tasks finishing first, etc. can change the numbers).
        public static Plan? GetPlan(AppDbContext context, AccountId accountId, VillageId sourceVillageId)
        {
            var sourceStorage = context.Storages.FirstOrDefault(x => x.VillageId == sourceVillageId.Value);
            if (sourceStorage is null) return null;

            var overflowPercent = context.ByName(sourceVillageId, VillageSettingEnums.AutoBalanceOverflowPercent);
            var targetPercent = context.ByName(sourceVillageId, VillageSettingEnums.AutoBalanceTargetPercent);

            var otherVillageIds = context.Villages
                .Where(x => x.AccountId == accountId.Value)
                .Where(x => x.Id != sourceVillageId.Value)
                .Select(x => x.Id)
                .AsEnumerable()
                .Select(id => new VillageId(id))
                .Where(id => context.BooleanByName(id, VillageSettingEnums.AutoBalanceEnable))
                .ToList();

            if (otherVillageIds.Count == 0) return null;

            string? worstResource = null;
            float worstPercent = 0;
            long worstAmount = 0;

            // find the single most-overflowing resource in this village first
            foreach (var resourceType in ResourceTypes)
            {
                var capacity = GetCapacity(sourceStorage, resourceType);
                if (capacity <= 0) continue;

                var current = GetAmount(sourceStorage, resourceType);
                var percent = current * 100f / capacity;
                if (percent < overflowPercent) continue;

                var downTo = capacity * targetPercent / 100;
                var amount = current - downTo;
                if (amount <= 0) continue;

                if (worstResource is null || percent > worstPercent)
                {
                    worstResource = resourceType;
                    worstPercent = percent;
                    worstAmount = amount;
                }
            }

            if (worstResource is null) return null;

            // now find the village with the most free room for that specific resource
            VillageId? bestTargetId = null;
            long bestRoom = 0;

            foreach (var targetId in otherVillageIds)
            {
                var storage = context.Storages.FirstOrDefault(s => s.VillageId == targetId.Value);
                if (storage is null) continue;

                var capacity = GetCapacity(storage, worstResource);
                if (capacity <= 0) continue;

                var current = GetAmount(storage, worstResource);
                var room = capacity - current;
                if (room <= 0) continue;

                if (bestTargetId is null || room > bestRoom)
                {
                    bestTargetId = targetId;
                    bestRoom = room;
                }
            }

            if (bestTargetId is null) return null;

            var amountToSend = Math.Min(worstAmount, bestRoom);
            if (amountToSend <= 0) return null;

            return new Plan(worstResource, bestTargetId.Value, amountToSend);
        }

        private static async ValueTask<Result> HandleAsync(
            Task task,
            AppDbContext context,
            ToSendResourcePageCommand.Handler toSendResourcePageCommand,
            SendResourceCommand.Handler sendResourceCommand,
            SaveVillageSettingCommand.Handler saveVillageSettingCommand,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            var plan = GetPlan(context, task.AccountId, task.VillageId);
            if (plan is null)
            {
                // Nothing to do anymore (storage dropped, or another village already covered it).
                return Skip.Error;
            }

            var result = await toSendResourcePageCommand.HandleAsync(new(task.VillageId), cancellationToken);
            if (result.IsFailed)
            {
                if (result.HasError<MissingBuilding>())
                {
                    var settings = new Dictionary<VillageSettingEnums, int>() {
                        { VillageSettingEnums.AutoBalanceEnable, 0 }
                    };
                    await saveVillageSettingCommand.HandleAsync(new(task.AccountId, task.VillageId, settings), cancellationToken);
                    logger.Warning("No marketplace in this village, disabling auto balance.");
                    return Skip.Error.WithErrors(result.Errors);
                }
                return result;
            }

            result = await sendResourceCommand.HandleAsync(new(task.VillageId, plan.TargetVillageId, plan.ResourceType, plan.Amount), cancellationToken);
            if (result.IsFailed) return result;

            return Result.Ok();
        }
    }
}
