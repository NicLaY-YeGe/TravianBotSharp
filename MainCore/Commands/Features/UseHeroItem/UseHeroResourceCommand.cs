namespace MainCore.Commands.Features.UseHeroItem
{
    [Handler]
    public static partial class UseHeroResourceCommand
    {
        public sealed record Command(AccountId AccountId, VillageId VillageId, long[] Resource) : IAccountVillageCommand
        {
            public void Deconstruct(out AccountId accountId, out VillageId villageId) => (accountId, villageId) = (AccountId, VillageId);
        }

        private static async ValueTask<Result> HandleAsync(
            Command command,
            AppDbContext context,
            ToHeroInventoryCommand.Handler toHeroInventoryCommand,
            UpdateInventoryCommand.Handler updateInventoryCommand,
            ValidateEnoughResourceCommand.Handler validateEnoughResourceCommand,
            UseHeroItemCommand.Handler useHeroItemCommand,
            IDelayService delayService,
            CancellationToken cancellationToken)
        {
            var (accountId, villageId, resource) = command;

            var result = await toHeroInventoryCommand.HandleAsync(new(), cancellationToken);
            if (result.IsFailed) return result;

            await updateInventoryCommand.HandleAsync(new(accountId), cancellationToken);

            resource = resource.Select(RoundUpTo100).ToArray();

            // Rounding up can push the target past the warehouse/granary CAPACITY even though
            // the un-rounded requirement was already confirmed to fit (HandleResourceCommand's
            // StorageLimit check runs before rounding). Clamp the excess so we never ask to
            // transfer more than what physically fits - the real need itself is never cut,
            // since capacity was already validated >= required >= this raw missing amount.
            resource = ClampToStorageCapacity(context, villageId, resource);

            result = await validateEnoughResourceCommand.HandleAsync(new(accountId, resource), cancellationToken);
            if (result.IsFailed) return result;

            var itemsToUse = new Dictionary<HeroItemEnums, long>
            {
                { HeroItemEnums.Wood, resource[0] },
                { HeroItemEnums.Clay, resource[1] },
                { HeroItemEnums.Iron, resource[2] },
                { HeroItemEnums.Crop, resource[3] },
            };

            result = await useHeroItemCommand.HandleAsync(new(itemsToUse), cancellationToken);
            if (result.IsFailed) return result;

            await delayService.DelayClick(cancellationToken);
            return Result.Ok();
        }

        private static long RoundUpTo100(long res)
        {
            if (res == 0) return 0;
            var remainder = res % 100;
            return res + (100 - remainder);
        }

        // Wood/Clay/Iron share the warehouse cap, Crop uses the granary cap. Storage.Warehouse
        // and Storage.Granary hold CAPACITY (not current fill) - see UpdateStorageCommand.
        private static long[] ClampToStorageCapacity(AppDbContext context, VillageId villageId, long[] resource)
        {
            var storage = context.Storages.FirstOrDefault(x => x.VillageId == villageId.Value);
            if (storage is null) return resource;

            var current = new[] { storage.Wood, storage.Clay, storage.Iron, storage.Crop };
            var capacity = new[] { storage.Warehouse, storage.Warehouse, storage.Warehouse, storage.Granary };

            var clamped = new long[resource.Length];
            for (var i = 0; i < resource.Length; i++)
            {
                var headroom = Math.Max(0, capacity[i] - current[i]);
                clamped[i] = Math.Min(resource[i], headroom);
            }
            return clamped;
        }
    }
}