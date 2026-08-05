using MainCore.UI.ViewModels.Abstract;
using System.Collections.ObjectModel;

namespace MainCore.UI.ViewModels.UserControls
{
    public partial class HourItem : ViewModelBase
    {
        public HourItem(int hour)
        {
            Hour = hour;
        }

        public int Hour { get; }

        public string Label => $"{Hour:00}";

        [Reactive]
        private bool _isChecked;
    }

    /// <summary>
    /// 24 independent toggles, one per hour of the day (0-23). Doesn't matter how they're
    /// laid out on screen - Hour is what maps a box back to a real clock hour, not its
    /// position in the list.
    /// </summary>
    public partial class HoursScheduleViewModel : ViewModelBase
    {
        public HoursScheduleViewModel()
        {
            for (var hour = 0; hour < 24; hour++)
            {
                Hours.Add(new HourItem(hour));
            }
        }

        public ObservableCollection<HourItem> Hours { get; } = new();

        public void Set(int mask)
        {
            foreach (var item in Hours)
            {
                item.IsChecked = (mask & (1 << item.Hour)) != 0;
            }
        }

        public int Get()
        {
            var mask = 0;
            foreach (var item in Hours)
            {
                if (item.IsChecked) mask |= 1 << item.Hour;
            }
            return mask;
        }

        public void SelectAll()
        {
            foreach (var item in Hours) item.IsChecked = true;
        }

        public void SelectNone()
        {
            foreach (var item in Hours) item.IsChecked = false;
        }
    }
}
