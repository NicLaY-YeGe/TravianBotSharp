using MainCore.Commands.Features.Scheduling;
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

                var overflowing = GetOverflowingResources(context, VillageId);
                if (overflowing.Count == 0) return false;

                // 2026-11-15: only queue the task when the hammer can really take a merchant load of an overflowing
                // resource (up to its own overflow percent). Before, a full hammer still cost a Marketplace visit per
                // storage update (22 runs in 11 h, most of them "nothing worth sending").
                var source = context.Storages.FirstOrDefault(x => x.VillageId == VillageId.Value);
                var hammer = context.Storages.FirstOrDefault(x => x.VillageId == hammerVillageId.Value.Value);
                if (source is null || hammer is null) return true; // unknown state: let the task look
                var sourcePercent = context.ByName(VillageId, VillageSettingEnums.OverflowToHammerPercent);
                var hammerCeiling = GetHammerCeilingPercent(context, hammerVillageId.Value);
                var floor = GetOwnFloor(source, VillageId);
                return overflowing.Any(r =>
                    Math.Min(GetOverflowAmount(source, r, sourcePercent, floor), ReceiveRoomRules.Room(GetCapacity(hammer, r), GetAmount(hammer, r), hammerCeiling))
                    >= MerchantAmountRules.LoadPerMerchant);
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

        // 2026-11-15: the hammer village is a RECEIVER here; if it has Auto balance on it ships resources away above its
        // own Auto balance overflow percent, so this task must not fill it past that (the log showed 20-33k crop going
        // back and forth between the hammer and a side village). Without Auto balance there is no limit but capacity.
        private static int GetHammerCeilingPercent(AppDbContext context, VillageId hammerId)
            => ReceiveRoomRules.CeilingPercent(
                context.BooleanByName(hammerId, VillageSettingEnums.AutoBalanceEnable),
                context.ByName(hammerId, VillageSettingEnums.AutoBalanceOverflowPercent),
                false,
                0);

        // 2026-11-15: every early exit used to be a silent Skip.Error with ExecuteAt unchanged ("Task failed / Bot skip
        // this task", 4x in the 2026-11-15 log, no reason). Say why and move the task out (same pattern as Balance resources).
        private static Result Defer(Task task, ILogger logger, string reason)
        {
            task.ExecuteAt = DateTime.Now.AddMinutes(Random.Shared.Next(20, 31));
            logger.Information("Overflow to hammer in {VillageId}: nothing to send ({Reason}), next look at {Time:HH:mm}.", task.VillageId, reason, task.ExecuteAt);
            return Result.Ok();
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

        // How much of this resource the village may send - this is both the weight used to split merchants across
        // resources, and the hard cap on how much of it makes sense to send. 2026-11-17: the task still STARTS at the
        // overflow percent, but it drains DOWN TO `OverflowDrainRules.DrainMarginPercent` points below it (it used to send
        // only what stood above the threshold, so the village crossed it again within ~19 minutes).
        // 2026-11-25: never into the cost of the village's own waiting build job (OutgoingFloorRules).
        private static long GetOverflowAmount(Storage storage, string resourceType, int overflowPercent, long[] floor)
        {
            var surplus = OverflowDrainRules.SendableAmount(GetCapacity(storage, resourceType), GetAmount(storage, resourceType), overflowPercent);
            return OutgoingFloorRules.Sendable(surplus, GetAmount(storage, resourceType), OutgoingFloorRules.FloorOf(floor, resourceType));
        }

        private static long[] GetOwnFloor(Storage storage, VillageId villageId)
            => OutgoingFloorRules.Floor(storage.Warehouse, storage.Granary, ConstructionNeed.GetWaiting(villageId));

        // 2026-10-25, user request: shipments are multiples of one merchant's load (750), rounded
        // DOWN (this is a surplus send - never more than asked). Was: nearest 100 with +/-3% jitter.
        private static long RoundHumanLike(double raw)
        {
            return MerchantAmountRules.RoundDown(raw);
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
            if (overflowing.Count == 0) return Defer(task, logger, "no resource is above the overflow percent any more");

            var hammerVillageId = GetHammerVillageId(context, task.AccountId, task.VillageId);
            if (hammerVillageId is null) return Defer(task, logger, "no hammer village is set for this account");

            var hammerVillage = context.Villages.FirstOrDefault(x => x.Id == hammerVillageId.Value.Value && x.AccountId == task.AccountId.Value);
            if (hammerVillage is null) return Defer(task, logger, "the hammer village was not found");

            var pageResult = await toSendResourcePageCommand.HandleAsync(new(task.VillageId), cancellationToken);
            if (pageResult.IsFailed)
            {
                if (pageResult.HasError<MissingBuilding>())
                {
                    logger.Warning("No marketplace in {VillageId}, cannot send overflow to hammer.", task.VillageId);
                    return Skip.Error.WithErrors(pageResult.Errors);
                }
                if (pageResult.HasError<Cancel>()) return pageResult;
                // Secondary task: a page problem (e.g. "Found 0 tabs", live log 17:29) must not Stop/pause
                // the account (CLAUDE.md rule 4). Skip, and try again in a few minutes.
                task.ExecuteAt = DateTime.Now.AddMinutes(Random.Shared.Next(3, 6));
                logger.Warning("Overflow to hammer in {VillageId}: could not open the send page, retrying at {Time:HH:mm}.", task.VillageId, task.ExecuteAt);
                return Result.Ok();
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
            var hammerCeilingPercent = GetHammerCeilingPercent(context, hammerVillageId.Value);
            var ownFloor = sourceStorage is null ? new long[4] : GetOwnFloor(sourceStorage, task.VillageId);

            var weights = new Dictionary<string, long>();
            var maxPerResource = new Dictionary<string, long>();
            foreach (var resource in overflowing)
            {
                var room = hammerStorage is null ? long.MaxValue : ReceiveRoomRules.Room(GetCapacity(hammerStorage, resource), GetAmount(hammerStorage, resource), hammerCeilingPercent);
                var overflowAmount = sourceStorage is null ? 0 : GetOverflowAmount(sourceStorage, resource, overflowPercent, ownFloor);

                weights[resource] = overflowAmount;
                maxPerResource[resource] = Math.Min(room, overflowAmount);
            }

            logger.Information("Overflow to hammer in {VillageId}: {Resources} at or above {Percent} %, draining down to {Target} %.", task.VillageId, string.Join("/", overflowing), overflowPercent, OverflowDrainRules.DrainTargetPercent(overflowPercent));

            if (ownFloor.Any(x => x > 0))
            {
                logger.Information("Overflow to hammer in {VillageId}: keeping {Floor} (wood/clay/iron/crop) for the build job that waits for resources.", task.VillageId, string.Join("/", ownFloor));
            }

            var amounts = DistributeProportionally(overflowing, weights, maxPerResource, totalToSend);
            if (amounts.Values.Sum() <= 0)
            {
                // 2026-10-25 (live log): this was a silent Skip.Error, and UpdateStorageCommand re-adds the
                // task after every storage update, so a village whose surplus (or the hammer's free room)
                // is below one 750 merchant load visited the Marketplace every ~80 s for nothing.
                // Say why, and move the task out so the re-add finds it already queued.
                task.ExecuteAt = DateTime.Now.AddMinutes(Random.Shared.Next(20, 31));
                logger.Information(
                    "Overflow to hammer in {VillageId}: nothing worth sending (surplus or the hammer's free room - up to its own overflow percent - is below one merchant load), next look at {Time:HH:mm}.",
                    task.VillageId, task.ExecuteAt);
                return Result.Ok();
            }

            var sendResult = await sendResourceCommand.HandleAsync(new(task.VillageId, hammerVillageId.Value, amounts), cancellationToken);
            // SendResourceCommand already classifies its failures (Skip for form problems, Cancel for
            // pause) - re-wrapping as Stop would pause the whole account over one missed transfer.
            if (sendResult.IsFailed) return sendResult;

            return Result.Ok();
        }
    }
}
