using MainCore.UI.Models.Output;
using MainCore.UI.ViewModels.Abstract;
using MainCore.UI.ViewModels.UserControls;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

namespace MainCore.UI.ViewModels.Tabs
{
    // CRUD UI for RaidListEntry (see RaidListTask.cs for the execution side). Until now rows
    // could only be inserted directly into the DB - this tab is the "add/toggle/delete row"
    // surface the rest of the app is missing (2026-08-18).
    //
    // Reuses WaveAttackVillageItem (source-village Id/Name pair for the ComboBox) from the
    // existing Sync/Wave Attack tabs rather than introducing a near-duplicate type - that shape
    // matches exactly what a single raid row needs. RaidListTroopSlotItem (Min/Max per slot,
    // 2026-08-22) is Raid List's own type though, NOT SyncAttackTroopSlotItem's single fixed
    // Amount - only Raid List rows re-roll a random amount per send (see
    // RaidListEntry.RollTroopAmounts), so the two shapes genuinely differ. Existing rows are
    // listed via the same ListBoxItemViewModel/ListBoxItem pattern FarmingViewModel uses for
    // its farm-list ListBox (Color: Green = active, Red = paused), so selection-based actions
    // (Toggle/Delete) follow the same SelectedItem convention as BuildViewModel's job list
    // instead of introducing per-row buttons, which isn't a pattern used elsewhere in this
    // codebase.
    [RegisterSingleton<RaidListViewModel>]
    public partial class RaidListViewModel : AccountTabViewModelBase
    {
        private readonly IDialogService _dialogService;
        private readonly ICustomServiceScopeFactory _serviceScopeFactory;
        private readonly ITaskManager _taskManager;

        // Matches "[40|-2]", "40|-2", "40, -2", " -12 | 30 " etc - one X/Y pair per line, in
        // whatever bracket/separator/whitespace shape a player would naturally paste from the
        // in-game map or a farm-target list someone shared. Anything a line doesn't match is
        // reported back to the user rather than silently dropped - see BulkAdd.
        private static readonly Regex CoordinateLinePattern = new(@"^\[?\s*(-?\d+)\s*[|,]\s*(-?\d+)\s*\]?$", RegexOptions.Compiled);

        public ObservableCollection<WaveAttackVillageItem> Villages { get; } = new();
        public ObservableCollection<RaidListTroopSlotItem> TroopSlots { get; } = new();
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

        // One coordinate per line - see CoordinateLinePattern. Uses the SAME village/troop
        // ranges/hero/interval settings above as the single Add form, so filling those in once
        // and pasting a big target list (e.g. a whole farm-target sheet) creates every row with
        // one click instead of repeating Add 20+ times by hand.
        [Reactive]
        private string _bulkTargets = "";

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

        public sealed record LoadResult(List<WaveAttackVillageItem> Villages, List<RaidListTroopSlotItem> Slots);

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
                .Select((troop, index) => new RaidListTroopSlotItem(index + 1, troop))
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
                    var troopSummary = string.Join(", ", entry.GetTroopAmountRanges()
                        .Select(kv => kv.Value.Min == kv.Value.Max
                            ? $"slot {kv.Key}: {kv.Value.Min}"
                            : $"slot {kv.Key}: {kv.Value.Min}-{kv.Value.Max}"));
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

        // Shared validation for the source village/troop ranges/interval - identical
        // requirements for a single Add and every row a BulkAdd creates. Returns null (after
        // showing the relevant message box) on the first problem found; the target coordinate
        // itself is validated separately by each caller since Add takes one pair from text
        // boxes and BulkAdd takes many from pasted lines.
        private async Task<(VillageId VillageId, Dictionary<int, TroopAmountRange> Ranges, int IntervalMin, int IntervalMax)?> ValidateSharedSettings()
        {
            if (SelectedVillage is null)
            {
                await _dialogService.MessageBox.Handle(new MessageBoxData("Error", "Select a source village."));
                return null;
            }

            var ranges = TroopSlots
                .Select(s => (s.Slot, Range: s.GetRange()))
                .Where(s => s.Range is not null)
                .ToDictionary(s => s.Slot, s => s.Range!.Value);

            if (ranges.Count == 0)
            {
                await _dialogService.MessageBox.Handle(new MessageBoxData("Error", "Enter at least one troop amount (Min, and optionally Max for a random range)."));
                return null;
            }

            if (!int.TryParse(IntervalMinMinutes, out var intervalMin) || intervalMin < 1)
            {
                await _dialogService.MessageBox.Handle(new MessageBoxData("Error", "Enter a valid minimum interval (1 or more minutes)."));
                return null;
            }

            if (!int.TryParse(IntervalMaxMinutes, out var intervalMax) || intervalMax < intervalMin)
            {
                await _dialogService.MessageBox.Handle(new MessageBoxData("Error", "Enter a valid maximum interval (greater than or equal to the minimum)."));
                return null;
            }

            return (SelectedVillage.VillageId, ranges, intervalMin, intervalMax);
        }

        private void InsertEntry(AppDbContext context, VillageId villageId, int x, int y, Dictionary<int, TroopAmountRange> ranges, int intervalMin, int intervalMax, DateTime nextExecuteAt)
        {
            var entry = new RaidListEntry()
            {
                AccountId = AccountId.Value,
                VillageId = villageId.Value,
                TargetX = x,
                TargetY = y,
                IncludeHero = IncludeHero,
                IntervalMinMinutes = intervalMin,
                IntervalMaxMinutes = intervalMax,
                NextExecuteAt = nextExecuteAt,
                IsActive = true,
            };
            entry.SetTroopAmountRanges(ranges);

            context.RaidListEntries.Add(entry);
            context.SaveChanges();

            _taskManager.Add(new RaidListTask.Task(AccountId, villageId, new RaidListEntryId(entry.Id))
            {
                ExecuteAt = nextExecuteAt,
            }, first: nextExecuteAt <= DateTime.Now);
        }

        [ReactiveCommand]
        private async Task Add()
        {
            if (!int.TryParse(TargetX, out var x) || !int.TryParse(TargetY, out var y))
            {
                await _dialogService.MessageBox.Handle(new MessageBoxData("Error", "Enter valid X/Y target coordinates."));
                return;
            }

            var validated = await ValidateSharedSettings();
            if (validated is null) return;
            var (villageId, ranges, intervalMin, intervalMax) = validated.Value;

            var now = DateTime.Now;

            using (var scope = _serviceScopeFactory.CreateScope(AccountId))
            {
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                // Queue it right away rather than waiting for the next UpdateStorageCommand
                // bootstrap pass (see its comment on RaidListEntries) - the row would otherwise
                // sit unscheduled until that village's next routine storage update.
                InsertEntry(context, villageId, x, y, ranges, intervalMin, intervalMax, now);
            }

            TargetX = "";
            TargetY = "";
            IncludeHero = false;
            foreach (var slot in TroopSlots) slot.Clear();

            await LoadEntriesCommand.Execute(AccountId);
            await _dialogService.MessageBox.Handle(new MessageBoxData("Information", "Raid added and scheduled."));
        }

        [ReactiveCommand]
        private async Task BulkAdd()
        {
            var lines = BulkTargets
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

            if (lines.Count == 0)
            {
                await _dialogService.MessageBox.Handle(new MessageBoxData("Error", "Paste at least one target coordinate, one per line."));
                return;
            }

            var targets = new List<(int X, int Y)>();
            var invalidLines = new List<string>();
            foreach (var line in lines)
            {
                var match = CoordinateLinePattern.Match(line);
                if (!match.Success)
                {
                    invalidLines.Add(line);
                    continue;
                }
                targets.Add((int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value)));
            }

            if (invalidLines.Count > 0)
            {
                await _dialogService.MessageBox.Handle(new MessageBoxData(
                    "Error",
                    $"Couldn't read {invalidLines.Count} line(s) as coordinates (expected something like \"40|-2\" or \"[40|-2]\"), fix or remove them and try again:\n{string.Join("\n", invalidLines.Take(10))}"));
                return;
            }

            var validated = await ValidateSharedSettings();
            if (validated is null) return;
            var (villageId, ranges, intervalMin, intervalMax) = validated.Value;

            var now = DateTime.Now;

            using (var scope = _serviceScopeFactory.CreateScope(AccountId))
            {
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                foreach (var (x, y) in targets)
                {
                    // Stagger the FIRST send too, not just every resend after it - random(0,
                    // IntervalMaxMinutes) from now, same window this row will keep using on
                    // every subsequent RescheduleNext. Without this every row from one bulk
                    // paste would hit the Rally Point in the same few seconds instead of
                    // spreading out like a normal raid list.
                    var firstDelayMinutes = Random.Shared.Next(0, intervalMax + 1);
                    var nextExecuteAt = now.AddMinutes(firstDelayMinutes);

                    InsertEntry(context, villageId, x, y, ranges, intervalMin, intervalMax, nextExecuteAt);
                }
            }

            BulkTargets = "";
            TargetX = "";
            TargetY = "";
            IncludeHero = false;
            foreach (var slot in TroopSlots) slot.Clear();

            await LoadEntriesCommand.Execute(AccountId);
            await _dialogService.MessageBox.Handle(new MessageBoxData("Information", $"Added {targets.Count} raid(s), staggered over the next {intervalMax} minutes."));
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
