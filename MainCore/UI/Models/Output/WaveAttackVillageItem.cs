namespace MainCore.UI.Models.Output
{
    // Source-village entry for the Wave Attack tab's ComboBox - unlike SyncAttack (which lets
    // several villages participate at once, one row each), a wave attack is always sent from a
    // single village, so this is just an Id/Name pair rather than a full row with its own troop
    // slots.
    public sealed record WaveAttackVillageItem(VillageId VillageId, string VillageName)
    {
        public override string ToString() => VillageName;
    }
}
