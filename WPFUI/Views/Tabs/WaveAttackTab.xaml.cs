using MainCore.Enums;
using MainCore.UI.ViewModels.Tabs;
using ReactiveUI;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using System.Windows;

namespace WPFUI.Views.Tabs
{
    public class WaveAttackTabBase : ReactiveUserControl<WaveAttackViewModel>
    {
    }

    /// <summary>
    /// Interaction logic for WaveAttackTab.xaml
    /// </summary>
    public partial class WaveAttackTab : WaveAttackTabBase
    {
        public WaveAttackTab()
        {
            InitializeComponent();
            this.WhenActivated(d =>
            {
                this.OneWayBind(ViewModel, vm => vm.Villages, v => v.VillageComboBox.ItemsSource).DisposeWith(d);
                this.Bind(ViewModel, vm => vm.SelectedVillage, v => v.VillageComboBox.SelectedItem).DisposeWith(d);

                this.OneWayBind(ViewModel, vm => vm.MainWaveSlots, v => v.MainWaveList.ItemsSource).DisposeWith(d);
                this.OneWayBind(ViewModel, vm => vm.RepeatWaveSlots, v => v.RepeatWaveList.ItemsSource).DisposeWith(d);

                this.Bind(ViewModel, vm => vm.TargetX, v => v.TargetX.Text).DisposeWith(d);
                this.Bind(ViewModel, vm => vm.TargetY, v => v.TargetY.Text).DisposeWith(d);
                this.Bind(ViewModel, vm => vm.MainWaveIncludeHero, v => v.MainWaveIncludeHeroCheckBox.IsChecked).DisposeWith(d);
                this.Bind(ViewModel, vm => vm.GapSeconds, v => v.GapSeconds.Text).DisposeWith(d);
                this.Bind(ViewModel, vm => vm.WaveCount, v => v.WaveCount.Text).DisposeWith(d);

                this.BindCommand(ViewModel, vm => vm.ScheduleCommand, v => v.ScheduleButton).DisposeWith(d);

                // Event type radios - wired by hand (routed events), same approach as
                // SyncAttackTab.xaml.cs and for the same reason (see that file's comment on why
                // Observable.FromEventPattern was avoided for tri-state radio groups).
                AttackNormalRadio.IsChecked = ViewModel?.EventType == RallyPointEventTypeEnums.AttackNormal;
                AttackRaidRadio.IsChecked = ViewModel?.EventType == RallyPointEventTypeEnums.AttackRaid;
                ReinforcementRadio.IsChecked = ViewModel?.EventType == RallyPointEventTypeEnums.Reinforcement;

                RoutedEventHandler attackNormalHandler = (_, __) =>
                {
                    if (ViewModel is not null) ViewModel.EventType = RallyPointEventTypeEnums.AttackNormal;
                };
                RoutedEventHandler attackRaidHandler = (_, __) =>
                {
                    if (ViewModel is not null) ViewModel.EventType = RallyPointEventTypeEnums.AttackRaid;
                };
                RoutedEventHandler reinforcementHandler = (_, __) =>
                {
                    if (ViewModel is not null) ViewModel.EventType = RallyPointEventTypeEnums.Reinforcement;
                };

                AttackNormalRadio.Checked += attackNormalHandler;
                AttackRaidRadio.Checked += attackRaidHandler;
                ReinforcementRadio.Checked += reinforcementHandler;

                Disposable.Create(() => AttackNormalRadio.Checked -= attackNormalHandler).DisposeWith(d);
                Disposable.Create(() => AttackRaidRadio.Checked -= attackRaidHandler).DisposeWith(d);
                Disposable.Create(() => ReinforcementRadio.Checked -= reinforcementHandler).DisposeWith(d);
            });
        }
    }
}
