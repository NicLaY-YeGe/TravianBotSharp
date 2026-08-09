using MainCore.Enums;
using MainCore.UI.ViewModels.Tabs;
using ReactiveUI;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using System.Windows;

namespace WPFUI.Views.Tabs
{
    public class SyncAttackTabBase : ReactiveUserControl<SyncAttackViewModel>
    {
    }

    /// <summary>
    /// Interaction logic for SyncAttackTab.xaml
    /// </summary>
    public partial class SyncAttackTab : SyncAttackTabBase
    {
        public SyncAttackTab()
        {
            InitializeComponent();
            this.WhenActivated(d =>
            {
                this.OneWayBind(ViewModel, vm => vm.Villages, v => v.VillageList.ItemsSource).DisposeWith(d);

                this.Bind(ViewModel, vm => vm.TargetX, v => v.TargetX.Text).DisposeWith(d);
                this.Bind(ViewModel, vm => vm.TargetY, v => v.TargetY.Text).DisposeWith(d);
                this.Bind(ViewModel, vm => vm.DesiredArrivalDate, v => v.DesiredArrivalDate.SelectedDate).DisposeWith(d);
                this.Bind(ViewModel, vm => vm.DesiredArrivalTime, v => v.DesiredArrivalTime.Text).DisposeWith(d);

                this.BindCommand(ViewModel, vm => vm.ScheduleCommand, v => v.ScheduleButton).DisposeWith(d);

                // Event type radios - wired by hand (routed events) rather than a converter-Bind,
                // since it's the most predictable way to two-way-map a tri-state radio group to
                // an enum without depending on a specific ReactiveUI overload being available.
                ReinforcementRadio.IsChecked = ViewModel?.EventType == RallyPointEventTypeEnums.Reinforcement;
                AttackNormalRadio.IsChecked = ViewModel?.EventType == RallyPointEventTypeEnums.AttackNormal;
                AttackRaidRadio.IsChecked = ViewModel?.EventType == RallyPointEventTypeEnums.AttackRaid;

                Observable.FromEventPattern<RoutedEventHandler, RoutedEventArgs>(
                        h => ReinforcementRadio.Checked += h, h => ReinforcementRadio.Checked -= h)
                    .Subscribe(_ => { if (ViewModel is not null) ViewModel.EventType = RallyPointEventTypeEnums.Reinforcement; })
                    .DisposeWith(d);

                Observable.FromEventPattern<RoutedEventHandler, RoutedEventArgs>(
                        h => AttackNormalRadio.Checked += h, h => AttackNormalRadio.Checked -= h)
                    .Subscribe(_ => { if (ViewModel is not null) ViewModel.EventType = RallyPointEventTypeEnums.AttackNormal; })
                    .DisposeWith(d);

                Observable.FromEventPattern<RoutedEventHandler, RoutedEventArgs>(
                        h => AttackRaidRadio.Checked += h, h => AttackRaidRadio.Checked -= h)
                    .Subscribe(_ => { if (ViewModel is not null) ViewModel.EventType = RallyPointEventTypeEnums.AttackRaid; })
                    .DisposeWith(d);

                // Arrival mode radios - same approach.
                EarliestRadio.IsChecked = ViewModel?.ArrivalMode == SyncAttackArrivalModeEnums.Earliest;
                SpecificRadio.IsChecked = ViewModel?.ArrivalMode == SyncAttackArrivalModeEnums.Specific;

                Observable.FromEventPattern<RoutedEventHandler, RoutedEventArgs>(
                        h => EarliestRadio.Checked += h, h => EarliestRadio.Checked -= h)
                    .Subscribe(_ => { if (ViewModel is not null) ViewModel.ArrivalMode = SyncAttackArrivalModeEnums.Earliest; })
                    .DisposeWith(d);

                Observable.FromEventPattern<RoutedEventHandler, RoutedEventArgs>(
                        h => SpecificRadio.Checked += h, h => SpecificRadio.Checked -= h)
                    .Subscribe(_ => { if (ViewModel is not null) ViewModel.ArrivalMode = SyncAttackArrivalModeEnums.Specific; })
                    .DisposeWith(d);
            });
        }
    }
}
