namespace MainCore.Commands.Update
{
    [Handler]
    public static partial class UpdateVillageListCommand
    {
        public sealed record Command(AccountId AccountId) : IAccountCommand;

        private static async ValueTask HandleAsync(
            Command command,
            IChromeBrowser browser,
            AppDbContext context,
            IRxQueue rxQueue,
            ITaskManager taskManager,
            ITelegramNotifier telegramNotifier)
        {
            await Task.CompletedTask;
            var accountId = command.AccountId;

            var dtos = VillagePanelParser.Get(browser.Html);
            if (!dtos.Any()) return;

            var previousAttackedVillageIds = context.Villages
                .Where(x => x.AccountId == accountId.Value && x.IsUnderAttack)
                .Select(x => x.Id)
                .ToHashSet();

            var newVillageIds = context.UpdateToDatabase(accountId, dtos.ToList());

            // §5l (2026-08-15): apply the account-wide default build template to every newly
            // founded village, exactly once. CanStart on the task itself waits until that
            // village's buildings have actually been scanned - see ApplyBuildTemplateTask.
            foreach (var newVillageId in newVillageIds)
            {
                taskManager.AddOrUpdate<ApplyBuildTemplateTask.Task>(new(accountId, newVillageId));
            }

            var newlyAttacked = dtos
                .Where(x => x.IsUnderAttack && !previousAttackedVillageIds.Contains(x.Id.Value))
                .ToList();

            if (newlyAttacked.Count > 0)
            {
                var telegramSetting = telegramNotifier.Get(accountId);
                if (telegramSetting.NotifyOnAttack)
                {
                    var username = context.Accounts.FirstOrDefault(x => x.Id == accountId.Value)?.Username ?? $"{accountId}";
                    foreach (var village in newlyAttacked)
                    {
                        await telegramNotifier.NotifyAsync(accountId, $"\U0001F6A8 {username} - {village.Name} saldiri altinda!");
                    }
                }

                foreach (var village in newlyAttacked)
                {
                    var dodgeTask = new DodgeTroopTask.Task(accountId, village.Id);
                    if (dodgeTask.CanStart(context) && !taskManager.IsExist<DodgeTroopTask.Task>(accountId, village.Id))
                    {
                        taskManager.Add(dodgeTask);
                    }
                }
            }

            rxQueue.Enqueue(new VillagesModified(accountId));

            var settingEnable = context.BooleanByName(accountId, AccountSettingEnums.EnableAutoLoadVillageBuilding);
            if (!settingEnable) return;

            var missingBuildingVillagesSpec = new MissingBuildingVillagesSpec(accountId);

            var villages = context.Villages
                .WithSpecification(missingBuildingVillagesSpec)
                .ToList();

            foreach (var village in villages)
            {
                if (taskManager.IsExist<UpdateBuildingTask.Task>(accountId, village)) continue;
                taskManager.AddOrUpdate<UpdateBuildingTask.Task>(new(accountId, village));
            }
        }

        private static List<VillageId> UpdateToDatabase(this AppDbContext context, AccountId accountId, List<VillageDto> dtos)
        {
            var villages = context.Villages
                .Where(x => x.AccountId == accountId.Value)
                .ToList();

            var ids = dtos.Select(x => x.Id.Value).ToList();

            var villageDeleted = villages.Where(x => !ids.Contains(x.Id)).ToList();
            var villageInserted = dtos.Where(x => !villages.Exists(v => v.Id == x.Id.Value)).ToList();
            var villageUpdated = villages.Where(x => ids.Contains(x.Id)).ToList();

            villageDeleted.ForEach(x => context.Remove(x));
            villageInserted.ForEach(x =>
            {
                context.Add(x.ToEntity(accountId));
                context.FillVillageSettings(accountId, x.Id);
            });

            foreach (var village in villageUpdated)
            {
                var dto = dtos.First(x => x.Id.Value == village.Id);
                dto.To(village);
                context.Update(village);
            }

            context.SaveChanges();

            return villageInserted.Select(x => x.Id).ToList();
        }
    }
}