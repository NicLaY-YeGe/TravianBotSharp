using MainCore.DTO;
using MainCore.Tasks;
using MainCore.UI.Models.Output;
using MainCore.UI.ViewModels.Abstract;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;

namespace MainCore.UI.ViewModels.Tabs
{
    [RegisterSingleton<SyncAttackViewModel>]
    public partial class SyncAttackViewModel : AccountTabViewModelBase
    {
        private readonly IDialogService _dialogService;
        private readonly ICustomServiceScopeFactory _serviceScopeFactory;
        private readonly ITaskManager _taskManager;

        public ObservableCollection<SyncAttackVillageRowItem> Villages { get; } = new();

        [Reactive]
        private string _targetX = "";

        [Reactive]
        private string _targetY = "";

        [Reactive]
        private RallyPointEventTypeEnums _eventType = RallyPointEventTypeEnums.Reinforcement;

        [Reactive]
        private SyncAttackArrivalModeEnums _arrivalMode = SyncAttackArrivalModeEnums.Earliest;

        // Nullable to match WPF's DatePicker.SelectedDate directly (avoids needing a Bind
        // converter in the code-behind).
        [Reactive]
        private DateTime? _desiredArrivalDate = DateTime.Now.Date;

        // HH:mm, kept as free text like the rest of the app's simple inputs (AmountInputUc etc.)
        [Reactive]
        private string _desiredArrivalTime = "12:00";

        public SyncAttackViewModel(IDialogService dialogService, ICustomServiceScopeFactory serviceScopeFactory, ITaskManager taskManager)
        {
            _dialogService = dialogService;
            _serviceScopeFactory = serviceScopeFactory;
            _taskManager = taskManager;
        }

        protected override Task Load(AccountId accountId)
        {
            using var scope = _serviceScopeFactory.CreateScope(accountId);
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var tribe = context.AccountsInfo
                .Where(x => x.AccountId == accountId.Value)
                .Select(x => x.Tribe)
                .FirstOrDefault();

            var villages = context.Villages
                .Where(x => x.AccountId == accountId.Value)
                .ToList();

            Villages.Clear();
            foreach (var village in villages)
            {
                Villages.Add(new SyncAttackVillageRowItem(new VillageId(village.Id), village.Name, tribe));
            }

            return Task.CompletedTask;
        }

        [ReactiveCommand]
        private async Task Schedule()
        {
            if (!int.TryParse(TargetX, out var x) || !int.TryParse(TargetY, out var y))
            {
                await _dialogService.MessageBox.Handle(new MessageBoxData("Error", "Enter valid X/Y target coordinates."));
                return;
            }

            var selected = Villages.Where(v => v.IsSelected && v.HasAnyTroops).ToList();
            if (selected.Count == 0)
            {
                await _dialogService.MessageBox.Handle(new MessageBoxData("Error",
                    "Select at least one village and enter at least one troop amount for it."));
                return;
            }

            DateTime? desiredArrival = null;
            if (ArrivalMode == SyncAttackArrivalModeEnums.Specific)
            {
                if (!TimeSpan.TryParse(DesiredArrivalTime, out var time))
                {
                    await _dialogService.MessageBox.Handle(new MessageBoxData("Error", "Enter a valid arrival time (HH:mm)."));
                    return;
                }

                desiredArrival = (DesiredArrivalDate ?? DateTime.Now.Date).Date.Add(time);
                if (desiredArrival <= DateTime.Now)
                {
                    await _dialogService.MessageBox.Handle(new MessageBoxData("Error", "Desired arrival time must be in the future."));
                    return;
                }
            }

            var plan = new SyncAttackPlan(
                x,
                y,
                EventType,
                ArrivalMode,
                desiredArrival,
                selected.Select(v => new SyncAttackVillageOrder(v.VillageId, v.GetTroopAmounts())).ToList());

            var task = new SyncAttackPlanTask.Task(AccountId, plan);
            _taskManager.Add(task, first: true);

            await _dialogService.MessageBox.Handle(new MessageBoxData("Information",
                $"Scheduled. The bot will probe travel times for {selected.Count} village(s), then send at the right " +
                "time for all of them to land together. Watch the task list / log for each village's computed send time."));
        }
    }
}
