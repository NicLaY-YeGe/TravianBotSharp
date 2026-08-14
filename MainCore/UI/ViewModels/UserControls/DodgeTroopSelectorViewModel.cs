using DynamicData;
using MainCore.Models;
using MainCore.UI.Models.Output;
using MainCore.UI.ViewModels.Abstract;
using System.Collections.ObjectModel;

namespace MainCore.UI.ViewModels.UserControls
{
    // Checkbox list of the 10 (tribe-relative) troop slots shown on the Rally Point "Send
    // troops" form, one checkbox per slot - lets the user pick MULTIPLE troop types for
    // Dodge to send together in a single attack movement (2026-08-13 evolution; replaces the
    // old single-slot AmountInputViewModel-based DodgeTroopSlot selector). Reuses the same
    // per-tribe slot ordering as SyncAttack's troop selector (RallyPointTroopSlots).
    public partial class DodgeTroopSelectorViewModel : ViewModelBase
    {
        public ObservableCollection<TroopCheckItem> Items { get; } = new();

        public int Get()
        {
            var mask = 0;
            foreach (var item in Items)
            {
                if (item.IsChecked) mask |= 1 << (item.Slot - 1);
            }
            return mask;
        }

        public void Set(int mask, TribeEnums tribe) => Rebuild(tribe, mask);

        // Called when the Tribe selector elsewhere on the form changes - rebuilds the list
        // for the new tribe's troop set while preserving which SLOT NUMBERS were checked.
        public void ChangeTribe(TribeEnums tribe) => Rebuild(tribe, Get());

        private void Rebuild(TribeEnums tribe, int mask)
        {
            Items.Clear();
            var slots = RallyPointTroopSlots.GetSlots(tribe);
            var items = new List<TroopCheckItem>();
            for (var i = 0; i < slots.Count; i++)
            {
                var slot = i + 1;
                var isChecked = (mask & (1 << (slot - 1))) != 0;
                items.Add(new TroopCheckItem(slot, slots[i], isChecked));
            }
            Items.AddRange(items);
        }
    }
}
