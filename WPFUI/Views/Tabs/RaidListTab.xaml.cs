using MainCore.UI.ViewModels.Tabs;
using ReactiveUI;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;

namespace WPFUI.Views.Tabs
{
    public class RaidListTabBase : ReactiveUserControl<RaidListViewModel>
    {
    }

    /// <summary>
    /// Interaction logic for RaidListTab.xaml
    /// </summary>
    public partial class RaidListTab : RaidListTabBase
    {
        public RaidListTab()
        {
            InitializeComponent();
            this.WhenActivated(d =>
            {
                this.OneWayBind(ViewModel, vm => vm.Villages, v => v.VillageComboBox.ItemsSource).DisposeWith(d);
                this.Bind(ViewModel, vm => vm.SelectedVillage, v => v.VillageComboBox.SelectedItem).DisposeWith(d);

                this.OneWayBind(ViewModel, vm => vm.TroopSlots, v => v.TroopSlotsList.ItemsSource).DisposeWith(d);

                this.Bind(ViewModel, vm => vm.TargetX, v => v.TargetX.Text).DisposeWith(d);
                this.Bind(ViewModel, vm => vm.TargetY, v => v.TargetY.Text).DisposeWith(d);
                this.Bind(ViewModel, vm => vm.IncludeHero, v => v.IncludeHeroCheckBox.IsChecked).DisposeWith(d);
                this.Bind(ViewModel, vm => vm.IntervalMinMinutes, v => v.IntervalMinMinutes.Text).DisposeWith(d);
                this.Bind(ViewModel, vm => vm.IntervalMaxMinutes, v => v.IntervalMaxMinutes.Text).DisposeWith(d);

                this.BindCommand(ViewModel, vm => vm.AddCommand, v => v.AddButton).DisposeWith(d);

                this.OneWayBind(ViewModel, vm => vm.Entries.Items, v => v.EntriesGrid.ItemsSource).DisposeWith(d);
                this.Bind(ViewModel, vm => vm.Entries.SelectedItem, v => v.EntriesGrid.SelectedItem).DisposeWith(d);
                this.Bind(ViewModel, vm => vm.Entries.SelectedIndex, v => v.EntriesGrid.SelectedIndex).DisposeWith(d);

                this.BindCommand(ViewModel, vm => vm.ToggleActiveCommand, v => v.ToggleActiveButton).DisposeWith(d);
                this.BindCommand(ViewModel, vm => vm.DeleteCommand, v => v.DeleteButton).DisposeWith(d);
            });
        }
    }
}
