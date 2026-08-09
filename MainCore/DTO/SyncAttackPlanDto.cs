namespace MainCore.DTO
{
    // One source village's contribution to a synchronized attack: which troop slots (1-10,
    // tribe-relative - see RallyPointTroopSlots) and how many of each to send.
    public sealed record SyncAttackVillageOrder(VillageId VillageId, IReadOnlyDictionary<int, long> TroopAmounts);

    // A full synchronized-arrival plan as configured by the user. TargetX/TargetY is any map
    // coordinate (not necessarily one of our own villages). DesiredArrivalTime is only used
    // when ArrivalMode is Specific.
    public sealed record SyncAttackPlan(
        int TargetX,
        int TargetY,
        RallyPointEventTypeEnums EventType,
        SyncAttackArrivalModeEnums ArrivalMode,
        DateTime? DesiredArrivalTime,
        IReadOnlyList<SyncAttackVillageOrder> Villages);
}
