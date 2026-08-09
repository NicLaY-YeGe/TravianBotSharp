using MainCore.Models;
using System.Collections.ObjectModel;

namespace MainCore.UI.Models.Output
{
    public partial class SyncAttackVillageRowItem : ReactiveObject
    {
        public VillageId VillageId { get; }
        public string VillageName { get; }

        [Reactive]
        private bool _isSelected;

        public ObservableCollection<SyncAttackTroopSlotItem> Slots { get; }

        public SyncAttackVillageRowItem(VillageId villageId, string villageName, TribeEnums tribe)
        {
            VillageId = villageId;
            VillageName = villageName;

            var slots = RallyPointTroopSlots.GetSlots(tribe);
            Slots = new ObservableCollection<SyncAttackTroopSlotItem>(
                slots.Select((troop, index) => new SyncAttackTroopSlotItem(index + 1, troop)));
        }

        public bool HasAnyTroops => Slots.Any(x => x.GetAmount() > 0);

        public Dictionary<int, long> GetTroopAmounts() =>
            Slots.Where(x => x.GetAmount() > 0).ToDictionary(x => x.Slot, x => x.GetAmount());
    }
}
