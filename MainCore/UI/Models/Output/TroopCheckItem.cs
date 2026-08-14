namespace MainCore.UI.Models.Output
{
    // One checkable row in a multi-select troop list (used by Dodge's troop-type picker,
    // 2026-08-13). Unlike SyncAttackTroopSlotItem (which captures a typed amount), this only
    // captures yes/no - Dodge always sends the FULL available stack of every checked slot.
    public partial class TroopCheckItem : ReactiveObject
    {
        public int Slot { get; }
        public TroopEnums Troop { get; }
        public string TroopName => Troop.ToString();

        [Reactive]
        private bool _isChecked;

        public TroopCheckItem(int slot, TroopEnums troop, bool isChecked = false)
        {
            Slot = slot;
            Troop = troop;
            IsChecked = isChecked;
        }
    }
}
