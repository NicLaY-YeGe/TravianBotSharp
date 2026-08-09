namespace MainCore.UI.Models.Output
{
    public class SyncAttackTroopSlotItem : ReactiveObject
    {
        public int Slot { get; }
        public TroopEnums Troop { get; }
        public string TroopName => Troop.ToString();

        [Reactive]
        private string _amount = "";

        public SyncAttackTroopSlotItem(int slot, TroopEnums troop)
        {
            Slot = slot;
            Troop = troop;
        }

        public long GetAmount() => long.TryParse(Amount, out var value) && value > 0 ? value : 0;
    }
}
