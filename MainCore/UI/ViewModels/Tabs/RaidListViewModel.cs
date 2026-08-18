using MainCore.UI.Models.Output;
using MainCore.UI.ViewModels.Abstract;
using MainCore.UI.ViewModels.UserControls;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;

namespace MainCore.UI.ViewModels.Tabs
{
    // CRUD UI for RaidListEntry (see RaidListTask.cs for the execution side). Until now rows
    // could only be inserted directly into the DB - this tab is the "add/toggle/delete row"
    // surface the rest of the app is missing (2026-08-18).
    //
    // Reuses WaveAttackVillageItem (source-village Id/Name pair for the ComboBox) and
    // SyncAttackTroopSlotItem (per-slot amount textbox) from the existing Sync/Wave Attack
    // tabs rather than introducing near-duplicate types - both shapes match exactly what a
    // single raid row needs. Existing rows are listed via the same ListBoxItemViewModel/
    // ListBoxItem pattern FarmingViewModel uses for its farm-list ListBox (Color: Green =
    // active, Red = paused), so selection-based actions (Toggle/Delete) follow the same
    // SelectedItem convention as BuildViewModel's job list instead of introducing per-row
    // buttons, which isn't a pattern used elsewhere in this codebase.
    [RegisterSingleton<RaidListViewModel>]
    public partial class RaidListViewModel : AccountTabViewModelBase
    {
        private readonly IDialogService _dialogService;
        private readonly ICustomServiceScopeFactory _serviceScopeFactory;
        private readonly ITaskManager _taskManager;

        public ObservableCollection<WaveAttackVillageItem> Villages { get; } = new();
        public ObservableCollection<SyncAttackTroopSlotItem> TroopSlots { get; } = new();
        public ListBoxItemViewModel Entries { get; } = new();

        [Reactive]
        private WaveAttackVillageItem? _selectedVillage;

        [Reactive]
        private string _targetX = "";

        [Reactive]
        private string _targetY = "";

        [Reactive]
        private bool _includeHero;

        [Reactive]
        private string _intervalMinMinutes = "30";

        [Reactive]
        private string _intervalMaxMinutes = "60";

        public RaidListViewModel(IDialogService dialogService, ICustomServiceScopeFactory serviceScopeFactory, ITaskManager taskManager)
        {
            _dialogService = dialogService;
            _serviceScopeFactory = serviceScopeFactory;
            _taskManager = taskManager;

            LoadVillagesCommand.Subscribe(items =>
            {
                var previouslySelected = SelectedVillage?.VillageId;

                Villages.Clear();
                foreach (var item in items.Villages) Villages.Add(item);

                TroopSlots.Clear();
                foreach (var slot in items.Slots) TroopSlots.Add(slot);

                SelectedVillage = Villages.FirstOrDefault(x => x.VillageId == previouslySelected) ?? Villages.FirstOrDefault();
            });

            LoadEntriesCommand.Subscribe(Entries.Load);
        }

        protected override async Task Load(AccountId accountId)
        {
            await LoadVillagesCommand.Execute(accountId);
            await LoadEntriesCommand.Execute(accountId);
        }

        public sealed record LoadResult(List<WaveAttackVillageItem> Villages, List<SyncAttackTroopSlotItem> Slots);

        [ReactiveCommand]
        private LoadResult LoadVillages(AccountId accountId)
        {
            using var scope = _serviceScopeFactory.CreateScope(accountId);
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // AccountsInfo.Tribe is never reliable (always TribeEnums.Any) - the real tribe
            // lives in the AccountSettingEnums.Tribe key-value setting. Same fix as
            // SyncAttackViewModel/WaveAttackViewModel/SmithyUpgradeTask (see CLAUDE.md).
            var tribe = (TribeEnums)context.ByName(accountId, AccountSettingEnums.Tribe);

            var villages = context.Villages
                .Where(x => x.AccountId == accountId.Value)
                .ToList()
                .Select(village => new WaveAttackVillageItem(new VillageId(village.Id), village.Name))
                .ToList();

            var slots = RallyPointTroopSlots.GetSlots(tribe)
                .Select((troop, index) => new SyncAttackTroopSlotItem(index + 1, troop))
                .ToList();

            return new LoadResult(villages, slots);
        }

        [ReactiveCommand]
        private List<ListBoxItem> LoadEntries(AccountId accountId)
        {
            using var scope = _serviceScopeFactory.CreateScope(accountId);
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var villageNames = context.Villages
                .Where(x => x.AccountId == accountId.Value)
                .ToDictionary(x => x.Id, x => x.Name);

            return context.RaidListEntries
                .Where(x => x.AccountId == accountId.Value)
                .ToList()
                .Select(entry =>
                {
                    var villageName = villageNames.TryGetValue(entry.VillageId, out var name) ? name : $"village #{entry.VillageId}";
                    var troopSummary = string.Join(", ", entry.GetTroopAmounts().Select(kv => $"slot {kv.Key}: {kv.Value}"));
                    if (string.IsNullOrEmpty(troopSummary)) troopSummary = "(no troops)";
                    var heroText = entry.IncludeHero ? " + hero" : "";

                    return new ListBoxItem()
                    {
                        Id = entry.Id,
                        Color = entry.IsActive ? SplatColor.Green : SplatColor.Red,
                        Content = $"{villageName} -> ({entry.TargetX}|{entry.TargetY}) | {troopSummary}{heroText} | every {entry.IntervalMinMinutes}-{entry.IntervalMaxMinutes}m | next: {entry.NextExecuteAt:g}",
                    };
                })
                .ToList();
        }

        [ReactiveCommand]
        private async Task Add()
        {
            if (SelectedVillage is null)
            {
                await _dialogService.MessageBox.Handle(new MessageBoxData("Error", "Select a source village."));
                return;
            }

            if (!int.TryParse(TargetX, out var x) || !int.TryParse(TargetY, out var y))
            {
                await _dialogService.MessageBox.Handle(new MessageBoxData("Error", "Enter valid X/Y target coordinates."));
                return;
            }

            var troopAmounts = TroopSlots
                .Where(s => s.GetAmount() > 0)
                .ToDictionary(s => s.Slot, s => s.GetAmount());

            if (troopAmounts.Count == 0)
            {
                await _dialogService.MessageBox.Handle(new MessageBoxData("Error", "Enter at least one troop amount."));
                return;
            }

            if (!int.TryParse(IntervalMinMinutes, out var intervalMin) || intervalMin < 1)
            {
                await _dialogService.MessageBox.Handle(new MessageBoxData("Error", "Enter a valid minimum interval (1 or more minutes)."));
                return;
            }

            if (!int.TryParse(IntervalMaxMinutes, out var intervalMax) || intervalMax < intervalMin)
            {
                await _dialogService.MessageBox.Handle(new MessageBoxData("Error", "Enter a valid maximum interval (greater than or equal to the minimum)."));
                return;
            }

            var villageId = SelectedVillage.VillageId;
            var now = DateTime.Now;

            using (var scope = _serviceScopeFactory.CreateScope(AccountId))
            {
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var entry = new RaidListEntry()
                {
                    AccountId = AccountId.Value,
                    VillageId = villageId.Value,
                    TargetX = x,
                    TargetY = y,
                    IncludeHero = IncludeHero,
                    IntervalMinMinutes = intervalMin,
                    IntervalMaxMinutes = intervalMax,
                    NextExecuteAt = now,
                    IsActive = true,
                };
                entry.SetTroopAmounts(troopAmounts);

                context.RaidListEntries.Add(entry);
                context.SaveChanges();

                // Queue it right away rather than waiting for the next UpdateStorageCommand
                // bootstrap pass (see its comment on RaidListEntries) - the row would otherwise
                // sit unscheduled until that village's next routine storage update.
                _taskManager.Add(new RaidListTask.Task(AccountId, villageId, new RaidListEntryId(entry.Id))
                {
                    ExecuteAt = now,
                }, first: true);
            }

            TargetX = "";
            TargetY = "";
            IncludeHero = false;
            foreach (var slot in TroopSlots) slot.Amount = "";

            await LoadEntriesCommand.Execute(AccountId);
            await _dialogService.MessageBox.Handle(new MessageBoxData("Information", "Raid added and scheduled."));
        }

        [ReactiveCommand]
        private async Task ToggleActive()
        {
            if (Entries.SelectedItem is null)
            {
                await _dialogService.MessageBox.Handle(new MessageBoxData("Warning", "Select a raid entry first."));
                return;
            }

            var entryId = Entries.SelectedItem.Id;

            using (var scope = _serviceScopeFactory.CreateScope(AccountId))
            {
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var entry = context.RaidListEntries.FirstOrDefault(x => x.Id == entryId);
                if (entry is null) return;

                entry.IsActive = !entry.IsActive;

                var queuedTask = _taskManager.GetTaskList(AccountId)
                    .OfType<RaidListTask.Task>()
                    .FirstOrDefault(t => t.EntryId.Value == entryId);

                if (entry.IsActive)
                {
                    if (entry.NextExecuteAt < DateTime.Now) entry.NextExecuteAt = DateTime.Now;
                    if (queuedTask is null)
                    {
                        _taskManager.Add(new RaidListTask.Task(AccountId, new VillageId(entry.VillageId), new RaidListEntryId(entry.Id))
                        {
                            ExecuteAt = entry.NextExecuteAt,
                        });
                    }
                }
                else
                {
                    if (queuedTask is not null) _taskManager.Remove(AccountId, queuedTask);
                }

                context.SaveChanges();
            }

            await LoadEntriesCommand.Execute(AccountId);
        }

        [ReactiveCommand]
        private async Task Delete()
        {
            if (Entries.SelectedItem is null)
            {
                await _dialogService.MessageBox.Handle(new MessageBoxData("Warning", "Select a raid entry first."));
                return;
            }

            var entryId = Entries.SelectedItem.Id;

            var confirm = await _dialogService.ConfirmBox.Handle(new MessageBoxData("Warning", "Delete this raid entry?"));
            if (!confirm) return;

            using (var scope = _serviceScopeFactory.CreateScope(AccountId))
            {
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var queuedTask = _taskManager.GetTaskList(AccountId)
                    .OfType<RaidListTask.Task>()
                    .FirstOrDefault(t => t.EntryId.Value == entryId);
                if (queuedTask is not null) _taskManager.Remove(AccountId, queuedTask);

                context.RaidListEntries
                    .Where(x => x.Id == entryId)
                    .ExecuteDelete();
            }

            await LoadEntriesCommand.Execute(AccountId);
        }
    }
}
