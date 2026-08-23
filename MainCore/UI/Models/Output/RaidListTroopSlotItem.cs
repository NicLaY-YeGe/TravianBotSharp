namespace MainCore.UI.Models.Output
{
    // Per-slot Min/Max text inputs for the Raid List tab, distinct from
    // SyncAttackTroopSlotItem's single fixed Amount box (SyncAttack/WaveAttack still send a
    // fixed amount every time - only Raid List rows re-roll a random amount per send, see
    // RaidListEntry.RollTroopAmounts). Leaving both blank/zero means this slot isn't part of
    // the row at all - see GetRange().
    public partial class RaidListTroopSlotItem : ReactiveObject
    {
        public int Slot { get; }
        public TroopEnums Troop { get; }
        public string TroopName => Troop.ToString();

        [Reactive]
        private string _amountMin = "";

        [Reactive]
        private string _amountMax = "";

        public RaidListTroopSlotItem(int slot, TroopEnums troop)
        {
            Slot = slot;
            Troop = troop;
        }

        // A blank Max reuses Min as a fixed (zero-width) amount, so filling in just one box
        // works exactly like the old single-Amount field did.
        public TroopAmountRange? GetRange()
        {
            var hasMin = long.TryParse(AmountMin, out var min) && min > 0;
            if (!hasMin) return null;

            var max = long.TryParse(AmountMax, out var parsedMax) && parsedMax > 0 ? parsedMax : min;
            if (max < min) (min, max) = (max, min);

            return new TroopAmountRange(min, max);
        }

        public void Clear()
        {
            AmountMin = "";
            AmountMax = "";
        }
    }
}
