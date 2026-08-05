using MainCore.UI.ViewModels.UserControls;
using ReactiveUI;
using System.Reactive.Disposables.Fluent;
using System.Windows;

namespace WPFUI.Views.UserControls
{
    public class HoursScheduleUcBase : ReactiveUserControl<HoursScheduleViewModel>
    {
    }

    /// <summary>
    /// Interaction logic for HoursScheduleUc.xaml
    /// </summary>
    public partial class HoursScheduleUc : HoursScheduleUcBase
    {
        public HoursScheduleUc()
        {
            InitializeComponent();
            this.WhenActivated(d =>
            {
                this.OneWayBind(ViewModel, vm => vm.Hours, v => v.HoursItems.ItemsSource).DisposeWith(d);
            });

            AllButton.Click += (s, e) => ViewModel?.SelectAll();
            NoneButton.Click += (s, e) => ViewModel?.SelectNone();
        }

        public static readonly DependencyProperty TextProperty =
            DependencyProperty.Register("Text", typeof(string), typeof(HoursScheduleUc), new PropertyMetadata(default(string)));

        public string Text
        {
            get => (string)GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }
    }
}
