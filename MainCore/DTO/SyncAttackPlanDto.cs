using MainCore.Commands.Features.SyncAttack;

namespace MainCore.DTO
{
    // One source village's contribution to a synchronized attack: which troop slots (1-10,
    // tribe-relative - see RallyPointTroopSlots) and how many of each to send. IncludeHero
    // (2026-10-11) sends the hero along (slot 11) - only meaningful for the one village the hero
    // is currently in; the probe includes it too, so the hero's own speed is part of the
    // measured travel time.
    public sealed record SyncAttackVillageOrder(VillageId VillageId, IReadOnlyDictionary<int, long> TroopAmounts, bool IncludeHero = false);

    // A full synchronized-arrival plan as configured by the user. TargetX/TargetY is any map
    // coordinate (not necessarily one of our own villages). DesiredArrivalTime is only used
    // when ArrivalMode is Specific. Wake (2026-10-03) = null means an ordinary plan; otherwise
    // the sends get a wake window (see WakeWindowRules) so they still go out in sleep/offline
    // hours.
    public sealed record SyncAttackPlan(
        int TargetX,
        int TargetY,
        RallyPointEventTypeEnums EventType,
        SyncAttackArrivalModeEnums ArrivalMode,
        DateTime? DesiredArrivalTime,
        IReadOnlyList<SyncAttackVillageOrder> Villages,
        WakeWindowOptions? Wake = null);
}
