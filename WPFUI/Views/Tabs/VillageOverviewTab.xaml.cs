using MainCore.UI.ViewModels.Tabs;
using ReactiveUI;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;

namespace WPFUI.Views.Tabs
{
    public class VillageOverviewTabBase : ReactiveUserControl<VillageOverviewViewModel>
    {
    }

    /// <summary>
    /// Interaction logic for VillageOverviewTab.xaml
    /// </summary>
    public partial class VillageOverviewTab : VillageOverviewTabBase
    {
        public VillageOverviewTab()
        {
            InitializeComponent();
            this.WhenActivated(d =>
            {
                this.BindCommand(ViewModel, vm => vm.ReadFromGameCommand, v => v.ReadButton).DisposeWith(d);
                this.BindCommand(ViewModel, vm => vm.ReloadCommand, v => v.ReloadButton).DisposeWith(d);
                this.OneWayBind(ViewModel, vm => vm.StatusText, v => v.StatusText.Text).DisposeWith(d);
                this.OneWayBind(ViewModel, vm => vm.Rows, v => v.RowsGrid.ItemsSource).DisposeWith(d);
            });
        }
    }
}
