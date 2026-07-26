using MainCore.Commands.Features.UseHeroItem;

namespace MainCore.Commands.Features.UpgradeBuilding
{
    [Handler]
    public static partial class HandleResourceCommand
    {
        public sealed record Command(AccountId AccountId, VillageId VillageId, NormalBuildPlan Plan) : IAccountVillageCommand
        {
            public void Deconstruct(out AccountId accountId, out VillageId villageId) => (accountId, villageId) = (AccountId, VillageId);
        }

        private static async ValueTask<Result> HandleAsync(
            Command command,
            AppDbContext context,
            UpdateStorageCommand.Handler updateStorageCommand,
            UseHeroResourceCommand.Handler useHeroResourceCommand,
            ValidateEnoughResourceCommand.Handler validateEnoughResourceCommand,
            GetMissingResourceCommand.Handler getMissingResourceCommand,
            ISettingService settingService,
            ITaskManager taskManager,
            IChromeBrowser browser,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            var (accountId, villageId, plan) = command;

            await updateStorageCommand.HandleAsync(new(accountId, villageId), cancellationToken);

            var requiredResource = GetRequiredResource(browser, plan.Type);

            var result = await validateEnoughResourceCommand.HandleAsync(new(villageId, requiredResource), cancellationToken);
            if (!result.IsFailed) return Result.Ok();

            if (result.HasError<LackOfFreeCrop>()) return result;
            if (result.HasError<StorageLimit>()) return result;

            RequestHammerSupplyIfNeeded(context, accountId, villageId, requiredResource, taskManager, logger);

            var useHeroResource = settingService.BooleanByName(villageId, VillageSettingEnums.UseHeroResourceForBuilding);
            if (!useHeroResource) return result;

            logger.Information("Don't have enough resource. Use resource in hero invetory to upgrade building");
            var missingResource = await getMissingResourceCommand.HandleAsync(new(villageId, requiredResource), cancellationToken);

            var url = browser.CurrentUrl;

            result = await useHeroResourceCommand.HandleAsync(new(accountId, missingResource), cancellationToken);
            await browser.Navigate(url, cancellationToken);
            if (result.IsFailed) return result;

            return Result.Ok();
        }

        // If this village opted in and a hammer village is configured, snapshot how much of
        // each resource is missing (with a random 5-10% buffer on top, so shipments don't
        // land at suspiciously exact round numbers) and queue a one-shot delivery from the
        // hammer village to cover it.
        private static void RequestHammerSupplyIfNeeded(
            AppDbContext context,
            AccountId accountId,
            VillageId villageId,
            long[] requiredResource,
            ITaskManager taskManager,
            ILogger logger)
        {
            var enabled = context.BooleanByName(villageId, VillageSettingEnums.SupplyFromHammerEnable);
            if (!enabled) return;

            var hammerVillageIdRaw = context.ByName(accountId, AccountSettingEnums.HammerVillageId);
            if (hammerVillageIdRaw <= 0 || hammerVillageIdRaw == villageId.Value) return;

            var hammerVillage = context.Villages.FirstOrDefault(x => x.Id == hammerVillageIdRaw && x.AccountId == accountId.Value);
            if (hammerVillage is null) return;

            var storage = context.Storages.FirstOrDefault(x => x.VillageId == villageId.Value);
            if (storage is null) return;

            var current = new long[] { storage.Wood, storage.Clay, storage.Iron, storage.Crop };
            var names = new[] { "wood", "clay", "iron", "crop" };
            var amounts = new Dictionary<string, long>();

            for (var i = 0; i < 4; i++)
            {
                var missing = requiredResource[i] - current[i];
                if (missing <= 0) continue;

                var bufferPercent = 5 + (Random.Shared.NextDouble() * 5); // 5-10%
                var buffered = (long)(missing * (1 + (bufferPercent / 100)));
                amounts[names[i]] = buffered;
            }

            if (amounts.Count == 0) return;

            var hammerVillageId = new VillageId(hammerVillage.Id);
            var task = new SupplyFromHammerTask.Task(accountId, hammerVillageId, villageId, amounts);

            if (!taskManager.IsExist<SupplyFromHammerTask.Task>(accountId, hammerVillageId))
            {
                logger.Information("Requesting hammer village {HammerVillageId} to supply village {VillageId}.", hammerVillageId, villageId);
                taskManager.Add(task);
            }
        }

        private static long[] GetRequiredResource(IChromeBrowser browser, BuildingEnums building)
        {
            var doc = browser.Html;

            var resources = UpgradeParser.GetRequiredResource(doc, building);
            if (resources is null || resources.Count != 5) return new long[5];

            var resourceBuilding = new long[5];
            for (var i = 0; i < 5; i++)
            {
                resourceBuilding[i] = resources[i].InnerText.ParseLong();
            }

            return resourceBuilding;
        }
    }
}