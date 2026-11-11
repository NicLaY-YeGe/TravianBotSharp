using DynamicData;
using MainCore.Commands.Features.TrainTroop;
using MainCore.UI.ViewModels.Abstract;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

namespace MainCore.UI.ViewModels.UserControls
{
    // One row of the training table: a troop with its own amount (min-max units) and its own
    // "not again before" interval (min-max minutes, 0/0 = none).
    public partial class TroopSlotViewModel : ViewModelBase
    {
        public TroopSlotViewModel(int slot)
        {
            Slot = slot;
        }

        public int Slot { get; }

        [Reactive]
        private string _name = string.Empty;

        public RangeInputViewModel Amount { get; } = new();
        public RangeInputViewModel Time { get; } = new();
    }

    // The per-troop rows of ONE training building (Barracks / Stable / Workshop). Rows are tribe
    // relative: row N is the Nth troop of the building for the village's tribe (TrainTroopSlots).
    // Only the rows that exist for the tribe are shown; the values of hidden rows are kept so a
    // tribe change in the form never loses them.
    public class TroopSlotsViewModel : ViewModelBase
    {
        private readonly BuildingEnums _building;
        private readonly List<TroopSlotViewModel> _all = new();

        public TroopSlotsViewModel(BuildingEnums building)
        {
            _building = building;
            for (var slot = 1; slot <= TrainTroopSlots.SlotCount(building); slot++)
            {
                _all.Add(new TroopSlotViewModel(slot));
            }
        }

        // The rows that exist for the current tribe.
        public ObservableCollection<TroopSlotViewModel> Items { get; } = new();

        // Loads the rows from the stored settings. A config saved before per-troop settings (one troop per
        // building) shows up as that troop's row (TrainTroopPlanner does the migration).
        public void Set(Dictionary<VillageSettingEnums, int> settings, TribeEnums tribe)
        {
            foreach (var row in _all)
            {
                row.Amount.Set(0, 0);
                row.Time.Set(0, 0);
            }

            var plans = TrainTroopPlanner.GetPlans(_building, tribe, key => settings.GetValueOrDefault(key));
            foreach (var plan in plans)
            {
                if (plan.Slot < 1 || plan.Slot > _all.Count) continue;
                var row = _all[plan.Slot - 1];
                row.Amount.Set(plan.AmountMin, plan.AmountMax);
                row.Time.Set(plan.RepeatMin, plan.RepeatMax);
            }

            ChangeTribe(tribe);
        }

        public void ChangeTribe(TribeEnums tribe)
        {
            var troops = TrainTroopSlots.Troops(_building, tribe);
            Items.Clear();
            for (var i = 0; i < _all.Count; i++)
            {
                if (i >= troops.Count) continue;
                _all[i].Name = Humanize(troops[i]);
                Items.Add(_all[i]);
            }
        }

        public void WriteTo(Dictionary<VillageSettingEnums, int> settings)
        {
            foreach (var row in _all)
            {
                var (amountMin, amountMax, repeatMin, repeatMax) = TrainTroopSlots.Keys(_building, row.Slot);
                var (aMin, aMax) = row.Amount.Get();
                var (tMin, tMax) = row.Time.Get();
                settings[amountMin] = aMin;
                settings[amountMax] = aMax;
                settings[repeatMin] = tMin;
                settings[repeatMax] = tMax;
            }
        }

        // The rows the validator should look at (hidden rows keep stale values that do not matter).
        public IEnumerable<TroopSlotViewModel> AllRows => _all;

        private static string Humanize(TroopEnums troop)
        {
            return Regex.Replace(troop.ToString(), "(?<=[a-z])(?=[A-Z])", " ");
        }
    }
}
