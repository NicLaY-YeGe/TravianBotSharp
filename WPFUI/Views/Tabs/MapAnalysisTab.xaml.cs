using MainCore.UI.ViewModels.Tabs;
using ReactiveUI;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;

namespace WPFUI.Views.Tabs
{
    public class MapAnalysisTabBase : ReactiveUserControl<MapAnalysisViewModel>
    {
    }

    /// <summary>
    /// Interaction logic for MapAnalysisTab.xaml
    /// </summary>
    public partial class MapAnalysisTab : MapAnalysisTabBase
    {
        public MapAnalysisTab()
        {
            InitializeComponent();
            this.WhenActivated(d =>
            {
                this.Bind(ViewModel, vm => vm.TargetX, v => v.TargetX.Text).DisposeWith(d);
                this.Bind(ViewModel, vm => vm.TargetY, v => v.TargetY.Text).DisposeWith(d);
                this.Bind(ViewModel, vm => vm.MaxResults, v => v.MaxResults.Text).DisposeWith(d);

                this.BindCommand(ViewModel, vm => vm.SearchCommand, v => v.SearchButton).DisposeWith(d);

                this.OneWayBind(ViewModel, vm => vm.Results, v => v.ResultsList.ItemsSource).DisposeWith(d);
            });
        }
    }
}
