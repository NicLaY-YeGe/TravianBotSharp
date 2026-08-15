using MainCore.DTO;
using MainCore.Models;
using MainCore.Tasks;
using MainCore.UI.Models.Output;
using MainCore.UI.ViewModels.Abstract;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;

namespace MainCore.UI.ViewModels.Tabs
{
    [RegisterSingleton<WaveAttackViewModel>]
    public partial class WaveAttackViewModel : AccountTabViewModelBase
    {
        private readonly IDialogService _dialogService;
        private readonly ICustomServiceScopeFactory _serviceScopeFactory;
        private readonly ITaskManager _taskManager;

        public ObservableCollection<WaveAttackVillageItem> Villages { get; } = new();

        // Main (opening) wave: heavy, exact amounts entered by the user - reuses the same
        // "amount textbox per troop slot" row already used by SyncAttack's village rows.
        public ObservableCollection<SyncAttackTroopSlotItem> MainWaveSlots { get; } = new();

        // Repeat wave: the SAME small composition sent WaveCount times, GapSeconds apart.
        public ObservableCollection<SyncAttackTroopSlotItem> RepeatWaveSlots { get; } = new();

        [Reactive]
        private WaveAttackVillageItem? _selectedVillage;

        [Reactive]
        private string _targetX = "";

        [Reactive]
        private string _targetY = "";

        [Reactive]
        private RallyPointEventTypeEnums _eventType = RallyPointEventTypeEnums.AttackNormal;

        [Reactive]
        private bool _mainWaveIncludeHero;

        // Seconds between one wave's arrival and the next's - applies both from the main wave
        // to the first repeat wave, and between consecutive repeat waves (a single field, as
        // laid out in the tab: it sits at the end of the main-wave row).
        [Reactive]
        private string _gapSeconds = "1";

        // How many repeat waves to send after the main wave (0 = main wave only).
        [Reactive]
        private string _waveCount = "0";

        public WaveAttackViewModel(IDialogService dialogService, ICustomServiceScopeFactory serviceScopeFactory, ITaskManager taskManager)
        {
            _dialogService = dialogService;
            _serviceScopeFactory = serviceScopeFactory;
            _taskManager = taskManager;

            LoadVillagesCommand.Subscribe(items =>
            {
                var previouslySelected = SelectedVillage?.VillageId;

                Villages.Clear();
                foreach (var item in items.Villages) Villages.Add(item);

                MainWaveSlots.Clear();
                foreach (var slot in items.Slots) MainWaveSlots.Add(slot);

                RepeatWaveSlots.Clear();
                foreach (var slot in items.Slots) RepeatWaveSlots.Add(new SyncAttackTroopSlotItem(slot.Slot, slot.Troop));

                SelectedVillage = Villages.FirstOrDefault(x => x.VillageId == previouslySelected) ?? Villages.FirstOrDefault();
            });
        }

        protected override async Task Load(AccountId accountId)
        {
            await LoadVillagesCommand.Execute(accountId);
        }

        public sealed record LoadResult(List<WaveAttackVillageItem> Villages, List<SyncAttackTroopSlotItem> Slots);

        [ReactiveCommand]
        private LoadResult LoadVillages(AccountId accountId)
        {
            using var scope = _serviceScopeFactory.CreateScope(accountId);
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // AccountsInfo.Tribe is never reliable (always TribeEnums.Any) - the real tribe
            // lives in the AccountSettingEnums.Tribe key-value setting instead. Same fix as
            // SyncAttackViewModel/SmithyUpgradeTask (see CLAUDE.md).
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
        private async Task Schedule()
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

            var mainAmounts = MainWaveSlots
                .Where(s => s.GetAmount() > 0)
                .ToDictionary(s => s.Slot, s => s.GetAmount());

            if (mainAmounts.Count == 0)
            {
                await _dialogService.MessageBox.Handle(new MessageBoxData("Error", "Enter at least one troop amount for the main wave."));
                return;
            }

            if (!int.TryParse(WaveCount, out var waveCount) || waveCount < 0)
            {
                await _dialogService.MessageBox.Handle(new MessageBoxData("Error", "Enter a valid wave count (0 or more)."));
                return;
            }

            if (!int.TryParse(GapSeconds, out var gapSeconds) || (waveCount > 0 && gapSeconds <= 0))
            {
                await _dialogService.MessageBox.Handle(new MessageBoxData("Error", "Enter a valid gap in seconds (greater than 0) when using repeat waves."));
                return;
            }

            var repeatAmounts = RepeatWaveSlots
                .Where(s => s.GetAmount() > 0)
                .ToDictionary(s => s.Slot, s => s.GetAmount());

            if (waveCount > 0 && repeatAmounts.Count == 0)
            {
                await _dialogService.MessageBox.Handle(new MessageBoxData("Error", "Wave count is greater than 0 but no repeat-wave troop amounts were entered."));
                return;
            }

            var plan = new WaveAttackPlan(
                SelectedVillage.VillageId,
                x,
                y,
                EventType,
                mainAmounts,
                MainWaveIncludeHero,
                repeatAmounts,
                waveCount,
                gapSeconds);

            var task = new WaveAttackPlanTask.Task(AccountId, SelectedVillage.VillageId, plan);
            _taskManager.Add(task, first: true);

            await _dialogService.MessageBox.Handle(new MessageBoxData("Information",
                $"Scheduled. The bot will probe travel times from {SelectedVillage.VillageName}, then send the main wave " +
                (waveCount > 0 ? $"followed by {waveCount} repeat wave(s) {gapSeconds}s apart. " : "only. ") +
                "Watch the task list / log for each wave's computed send time."));
        }
    }
}
