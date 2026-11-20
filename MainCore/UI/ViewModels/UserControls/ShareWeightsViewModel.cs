using MainCore.Commands.Features.ShareTrain;
using MainCore.Commands.Features.TrainTroop;
using MainCore.UI.ViewModels.Abstract;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

namespace MainCore.UI.ViewModels.UserControls
{
    // One row of the share table: a troop and its weight (0 = not trained in the share mode).
    public partial class ShareWeightRowViewModel : ViewModelBase
    {
        public ShareWeightRowViewModel(int slot)
        {
            Slot = slot;
        }

        public int Slot { get; }

        [Reactive]
        private string _name = string.Empty;

        public AmountInputViewModel Weight { get; } = new();
    }

    // The weight rows of ONE training building (Barracks / Stable / Workshop); rows are tribe relative exactly like
    // TroopSlotsViewModel (row N = Nth troop of the building for the tribe, hidden rows keep their values).
    public class ShareWeightsViewModel : ViewModelBase
    {
        private readonly BuildingEnums _building;
        private readonly List<ShareWeightRowViewModel> _all = new();

        public ShareWeightsViewModel(BuildingEnums building)
        {
            _building = building;
            for (var slot = 1; slot <= TrainTroopSlots.SlotCount(building); slot++)
            {
                _all.Add(new ShareWeightRowViewModel(slot));
            }
        }

        public ObservableCollection<ShareWeightRowViewModel> Items { get; } = new();

        public IEnumerable<ShareWeightRowViewModel> AllRows => _all;

        public void Set(Dictionary<VillageSettingEnums, int> settings, TribeEnums tribe)
        {
            foreach (var row in _all)
            {
                row.Weight.Set(settings.GetValueOrDefault(ShareTrainSlots.Key(_building, row.Slot)));
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
                _all[i].Name = Regex.Replace(troops[i].ToString(), "(?<=[a-z])(?=[A-Z])", " ");
                Items.Add(_all[i]);
            }
        }

        public void WriteTo(Dictionary<VillageSettingEnums, int> settings)
        {
            foreach (var row in _all)
            {
                settings[ShareTrainSlots.Key(_building, row.Slot)] = Math.Clamp(row.Weight.Get(), 0, ShareTrainRules.MaxWeight);
            }
        }
    }
}
