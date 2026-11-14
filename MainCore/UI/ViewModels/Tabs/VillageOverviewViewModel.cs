using MainCore.Commands.Features.VillageOverview;
using MainCore.UI.Models.Output;
using MainCore.UI.ViewModels.Abstract;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;

namespace MainCore.UI.ViewModels.Tabs
{
    // 2026-11-11, user request: all villages of the selected account in one table - resources, troops (at home /
    // reinforcements / outside), merchants, the training queues of Barracks / Stable / Workshop, Smithy and Celebration.
    //
    // Where the numbers come from (nothing here touches the browser):
    //   - resources, Smithy and Celebration: the database and the village settings (always current);
    //   - troops, merchants, training queue ends: VillageOverviewStore, filled by page reads the bot already does (Marketplace
    //     visits, training visits) and by "Read from game", which queues RefreshVillageOverviewTask. They can be old - the
    //     "Troops read" column says how old the troop numbers are, "?" means never read since the app started.
    // The table is rebuilt when the tab opens, on "Reload table" and every 15 s while the tab is visible.
    // Great Barracks / Great Stable are not part of the table (user decision).
    [RegisterSingleton<VillageOverviewViewModel>]
    public partial class VillageOverviewViewModel : AccountTabViewModelBase
    {
        private static readonly BuildingEnums[] TrainingBuildings = [BuildingEnums.Barracks, BuildingEnums.Stable, BuildingEnums.Workshop];

        private readonly ICustomServiceScopeFactory _serviceScopeFactory;
        private readonly ITaskManager _taskManager;

        public ObservableCollection<VillageOverviewRow> Rows { get; } = new();

        [Reactive]
        private string _statusText = "";

        public VillageOverviewViewModel(ICustomServiceScopeFactory serviceScopeFactory, ITaskManager taskManager)
        {
            _serviceScopeFactory = serviceScopeFactory;
            _taskManager = taskManager;

            // command outputs are delivered on the UI thread (CLAUDE.md rule 10)
            LoadRowsCommand.Subscribe(rows =>
            {
                if (!Rows.SequenceEqual(rows))
                {
                    Rows.Clear();
                    foreach (var row in rows) Rows.Add(row);
                }
                StatusText = $"{rows.Count} village(s), table updated {DateTime.Now:HH:mm:ss}.";
            });

            // keep the table fresh while it is on screen; InvokeCommand skips a tick while the previous load still runs
            Observable.Interval(TimeSpan.FromSeconds(15))
                .Where(_ => IsActive && AccountId != AccountId.Empty)
                .Select(_ => AccountId)
                .ObserveOn(RxApp.TaskpoolScheduler)
                .InvokeCommand(LoadRowsCommand);
        }

        protected override async Task Load(AccountId accountId)
        {
            await LoadRowsCommand.Execute(accountId);
        }

        [ReactiveCommand]
        private async Task Reload()
        {
            if (AccountId == AccountId.Empty) return;
            await LoadRowsCommand.Execute(AccountId);
        }

        // Queues RefreshVillageOverviewTask at the front of the account's queue. It only runs while the account is online:
        // it needs the browser.
        [ReactiveCommand]
        private void ReadFromGame()
        {
            if (AccountId == AccountId.Empty) return;

            if (_taskManager.GetStatus(AccountId) != StatusEnums.Online)
            {
                StatusText = "The account has to be online (started) to read the villages from the game.";
                return;
            }

            if (_taskManager.IsExist<RefreshVillageOverviewTask.Task>(AccountId))
            {
                StatusText = "Reading is already queued - the table fills in as the villages are read.";
                return;
            }

            _taskManager.Add(new RefreshVillageOverviewTask.Task(AccountId), first: true);
            StatusText = "Queued: the villages are read one by one (troops, merchants, training queues).";
        }

        [ReactiveCommand]
        private List<VillageOverviewRow> LoadRows(AccountId accountId)
        {
            using var scope = _serviceScopeFactory.CreateScope(accountId);
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var now = DateTime.Now;

            var villages = context.Villages
                .AsNoTracking()
                .Where(x => x.AccountId == accountId.Value)
                .OrderBy(x => x.Name)
                .ToList();
            var ids = villages.Select(x => x.Id).ToList();

            var storages = context.Storages
                .AsNoTracking()
                .Where(x => ids.Contains(x.VillageId))
                .ToList()
                .GroupBy(x => x.VillageId)
                .ToDictionary(x => x.Key, x => x.First());

            var existing = context.Buildings
                .AsNoTracking()
                .Where(x => ids.Contains(x.VillageId) && x.Level > 0)
                .Select(x => new { x.VillageId, x.Type })
                .ToList()
                .Where(x => TrainingBuildings.Contains(x.Type))
                .Select(x => (x.VillageId, x.Type))
                .ToHashSet();

            // Construction (2026-11-12): one query for the running jobs of all villages, one for the build-list sizes and
            // one for "village page was read" (any Buildings row). Both tables are filled by page reads the bot does anyway.
            var queueRows = context.QueueBuildings
                .AsNoTracking()
                .Where(x => ids.Contains(x.VillageId))
                .ToList()
                .Where(x => x.CompleteTime > now)
                .GroupBy(x => x.VillageId)
                .ToDictionary(
                    x => x.Key,
                    x => x.Select(q => new VillageOverviewRules.ConstructionItem(q.Type.ToString(), q.Level, q.CompleteTime)).ToList());

            var waitingJobs = context.Jobs
                .AsNoTracking()
                .Where(x => ids.Contains(x.VillageId))
                .Select(x => x.VillageId)
                .ToList()
                .GroupBy(x => x)
                .ToDictionary(x => x.Key, x => x.Count());

            var villagesWithBuildings = context.Buildings
                .AsNoTracking()
                .Where(x => ids.Contains(x.VillageId))
                .Select(x => x.VillageId)
                .Distinct()
                .ToList()
                .ToHashSet();

            var rows = new List<VillageOverviewRow>();
            foreach (var village in villages)
            {
                var villageId = new VillageId(village.Id);
                storages.TryGetValue(village.Id, out var storage);
                var troops = VillageOverviewStore.GetTroops(village.Id);
                var merchants = VillageOverviewStore.GetMerchants(village.Id);

                string Queue(BuildingEnums building) => existing.Contains((village.Id, building))
                    ? VillageOverviewRules.FormatQueue(VillageOverviewStore.GetTrainQueue(village.Id, (int)building), now)
                    : VillageOverviewRules.None;

                var smithy = VillageOverviewRules.FormatBusyUntil(
                    context.BooleanByName(villageId, VillageSettingEnums.SmithyUpgradeEnable),
                    context.ByName(villageId, VillageSettingEnums.SmithyUpgradeBusyUntilUnixTime),
                    now);
                var celebration = VillageOverviewRules.FormatBusyUntil(
                    context.BooleanByName(villageId, VillageSettingEnums.SmallCelebrationEnable),
                    context.ByName(villageId, VillageSettingEnums.SmallCelebrationBusyUntilUnixTime),
                    now);

                rows.Add(new VillageOverviewRow(
                    village.IsUnderAttack ? $"{village.Name} (UNDER ATTACK)" : village.Name,
                    storage is null ? VillageOverviewRules.Unknown : VillageOverviewRules.FormatStock(storage.Wood, storage.Warehouse),
                    storage is null ? VillageOverviewRules.Unknown : VillageOverviewRules.FormatStock(storage.Clay, storage.Warehouse),
                    storage is null ? VillageOverviewRules.Unknown : VillageOverviewRules.FormatStock(storage.Iron, storage.Warehouse),
                    storage is null ? VillageOverviewRules.Unknown : VillageOverviewRules.FormatStock(storage.Crop, storage.Granary),
                    storage is null ? VillageOverviewRules.Unknown : VillageOverviewRules.FormatCropBalance(storage.FreeCrop),
                    VillageOverviewRules.FormatConstruction(
                        queueRows.TryGetValue(village.Id, out var running) ? running : [],
                        waitingJobs.TryGetValue(village.Id, out var waiting) ? waiting : 0,
                        villagesWithBuildings.Contains(village.Id),
                        now),
                    VillageOverviewRules.HomeText(troops),
                    VillageOverviewRules.ReinforcementText(troops),
                    VillageOverviewRules.OutsideText(troops),
                    troops is null ? "never" : VillageOverviewRules.FormatAgo(troops.ReadAt, now),
                    VillageOverviewRules.FormatMerchants(merchants),
                    Queue(BuildingEnums.Barracks),
                    Queue(BuildingEnums.Stable),
                    Queue(BuildingEnums.Workshop),
                    smithy,
                    celebration));
            }

            return rows;
        }
    }
}
