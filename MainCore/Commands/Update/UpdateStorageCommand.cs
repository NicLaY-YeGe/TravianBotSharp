namespace MainCore.Commands.Update
{
    [Handler]
    public static partial class UpdateStorageCommand
    {
        public sealed record Command(AccountId AccountId, VillageId VillageId) : IAccountVillageCommand;

        private static async ValueTask HandleAsync(
            Command command,
            AppDbContext context,
            IChromeBrowser browser,
            ITaskManager taskManager,
            ITelegramNotifier telegramNotifier
            )
        {
            await Task.CompletedTask;
            var (accountId, villageId) = command;

            var dto = Get(browser.Html);
            context.UpdateStorage(villageId, dto);

            // Checked on every visit (dorf1/dorf2 is loaded constantly during normal
            // operation), which is far more responsive than waiting on a village-list
            // refresh for the "under attack" flag.
            var village = context.Villages.FirstOrDefault(x => x.Id == villageId.Value);
            if (village is not null)
            {
                var wasUnderAttack = village.IsUnderAttack;
                var attackSeconds = MovementsParser.GetIncomingAttackSeconds(browser.Html);
                var isUnderAttack = attackSeconds is not null;

                if (village.IsUnderAttack != isUnderAttack)
                {
                    village.IsUnderAttack = isUnderAttack;
                    context.SaveChanges();
                }

                if (isUnderAttack && !wasUnderAttack)
                {
                    var telegramSetting = telegramNotifier.Get(accountId);
                    if (telegramSetting.NotifyOnAttack)
                    {
                        var username = context.Accounts.FirstOrDefault(x => x.Id == accountId.Value)?.Username ?? $"{accountId}";
                        await telegramNotifier.NotifyAsync(accountId, $"\U0001F6A8 {username} - {village.Name} saldiri altinda! ({attackSeconds}s)");
                    }

                    var dodgeTask = new DodgeTroopTask.Task(accountId, villageId);
                    if (dodgeTask.CanStart(context) && !taskManager.IsExist<DodgeTroopTask.Task>(accountId, villageId))
                    {
                        taskManager.Add(dodgeTask);
                    }
                }
            }

            var task = new NpcTask.Task(accountId, villageId);
            if (task.CanStart(context) && !taskManager.IsExist<NpcTask.Task>(accountId, villageId))
            {
                taskManager.Add(task);
            }

            var sendCropTask = new SendCropTask.Task(accountId, villageId);
            if (sendCropTask.CanStart(context) && !taskManager.IsExist<SendCropTask.Task>(accountId, villageId))
            {
                taskManager.Add(sendCropTask);
            }

            var balanceResourceTask = new BalanceResourceTask.Task(accountId, villageId);
            if (balanceResourceTask.CanStart(context) && !taskManager.IsExist<BalanceResourceTask.Task>(accountId, villageId))
            {
                taskManager.Add(balanceResourceTask);
            }

            var smithyUpgradeTask = new SmithyUpgradeTask.Task(accountId, villageId);
            if (smithyUpgradeTask.CanStart(context) && !taskManager.IsExist<SmithyUpgradeTask.Task>(accountId, villageId))
            {
                taskManager.Add(smithyUpgradeTask);
            }

            var demolishTask = new DemolishTask.Task(accountId, villageId);
            if (demolishTask.CanStart(context) && !taskManager.IsExist<DemolishTask.Task>(accountId, villageId))
            {
                taskManager.Add(demolishTask);
            }
        }

        private static StorageDto Get(HtmlDocument doc)
        {
            var storage = new StorageDto()
            {
                Wood = StorageParser.GetWood(doc),
                Clay = StorageParser.GetClay(doc),
                Iron = StorageParser.GetIron(doc),
                Crop = StorageParser.GetCrop(doc),
                FreeCrop = StorageParser.GetFreeCrop(doc),
                Warehouse = StorageParser.GetWarehouseCapacity(doc),
                Granary = StorageParser.GetGranaryCapacity(doc)
            };
            return storage;
        }

        private static void UpdateStorage(this AppDbContext context, VillageId villageId, StorageDto dto)
        {
            var dbStorage = context.Storages
                .Where(x => x.VillageId == villageId.Value)
                .FirstOrDefault();

            if (dbStorage is null)
            {
                var storage = dto.ToEntity(villageId);
                context.Add(storage);
            }
            else
            {
                dto.To(dbStorage);
            }

            context.SaveChanges();
        }
    }
}