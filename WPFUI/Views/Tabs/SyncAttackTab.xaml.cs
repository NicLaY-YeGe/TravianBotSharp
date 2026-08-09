using MainCore.Enums;
using MainCore.UI.ViewModels.Tabs;
using ReactiveUI;
using System.Reactive.Disposables;
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
                // Plain RoutedEventHandler wiring (not Rx's Observable.FromEventPattern) is used
                // here deliberately: FromEventPattern's <TDelegate, TEventArgs> overload resolution
                // proved fragile against this ReactiveUI/System.Reactive package combination
                // (CS-level "Cannot convert lambda expression to type 'IObserver<...>'" build
                // failures), so we avoid it entirely in favor of the ordinary event pattern that
                // every other WPF control in this codebase already relies on.
                ReinforcementRadio.IsChecked = ViewModel?.EventType == RallyPointEventTypeEnums.Reinforcement;
                AttackNormalRadio.IsChecked = ViewModel?.EventType == RallyPointEventTypeEnums.AttackNormal;
                AttackRaidRadio.IsChecked = ViewModel?.EventType == RallyPointEventTypeEnums.AttackRaid;

                RoutedEventHandler reinforcementHandler = (_, __) =>
                {
                    if (ViewModel is not null) ViewModel.EventType = RallyPointEventTypeEnums.Reinforcement;
                };
                RoutedEventHandler attackNormalHandler = (_, __) =>
                {
                    if (ViewModel is not null) ViewModel.EventType = RallyPointEventTypeEnums.AttackNormal;
                };
                RoutedEventHandler attackRaidHandler = (_, __) =>
                {
                    if (ViewModel is not null) ViewModel.EventType = RallyPointEventTypeEnums.AttackRaid;
                };

                ReinforcementRadio.Checked += reinforcementHandler;
                AttackNormalRadio.Checked += attackNormalHandler;
                AttackRaidRadio.Checked += attackRaidHandler;

                Disposable.Create(() => ReinforcementRadio.Checked -= reinforcementHandler).DisposeWith(d);
                Disposable.Create(() => AttackNormalRadio.Checked -= attackNormalHandler).DisposeWith(d);
                Disposable.Create(() => AttackRaidRadio.Checked -= attackRaidHandler).DisposeWith(d);

                // Arrival mode radios - same approach.
                EarliestRadio.IsChecked = ViewModel?.ArrivalMode == SyncAttackArrivalModeEnums.Earliest;
                SpecificRadio.IsChecked = ViewModel?.ArrivalMode == SyncAttackArrivalModeEnums.Specific;

                RoutedEventHandler earliestHandler = (_, __) =>
                {
                    if (ViewModel is not null) ViewModel.ArrivalMode = SyncAttackArrivalModeEnums.Earliest;
                };
                RoutedEventHandler specificHandler = (_, __) =>
                {
                    if (ViewModel is not null) ViewModel.ArrivalMode = SyncAttackArrivalModeEnums.Specific;
                };

                EarliestRadio.Checked += earliestHandler;
                SpecificRadio.Checked += specificHandler;

                Disposable.Create(() => EarliestRadio.Checked -= earliestHandler).DisposeWith(d);
                Disposable.Create(() => SpecificRadio.Checked -= specificHandler).DisposeWith(d);
            });
        }
    }
}
