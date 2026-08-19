namespace MainCore.Enums
{
    public enum VillageSettingEnums
    {
        // Building
        UseHeroResourceForBuilding = 1,

        ApplyRomanQueueLogicWhenBuilding,
        UseSpecialUpgrade,

        // Complete now
        CompleteImmediately,

        // General
        Tribe,

        // Train troop
        TrainTroopEnable,

        TrainTroopRepeatTimeMin,
        TrainTroopRepeatTimeMax,
        TrainWhenLowResource,

        BarrackTroop,
        BarrackAmountMin,
        BarrackAmountMax,

        StableTroop,
        StableAmountMin,
        StableAmountMax,

        GreatBarrackTroop,
        GreatBarrackAmountMin,
        GreatBarrackAmountMax,

        GreatStableTroop,
        GreatStableAmountMin,
        GreatStableAmountMax,

        WorkshopTroop,
        WorkshopAmountMin,
        WorkshopAmountMax,

        // NPC
        AutoNPCEnable,

        AutoNPCOverflow,
        AutoNPCGranaryPercent,
        AutoNPCWood,
        AutoNPCClay,
        AutoNPCIron,
        AutoNPCCrop,

        // Refresh
        AutoRefreshEnable,

        AutoRefreshMin,
        AutoRefreshMax,

        // Claim quest
        AutoClaimQuestEnable,

        CompleteImmediatelyTime,

        // NPC trigger direction
        AutoNPCReverse,

        // Auto send crop between own villages
        // Put this on the village that should RECEIVE crop when it's running low
        AutoSendCropEnable,

        AutoSendCropGranaryPercent,

        // Put this on villages that are allowed to SEND crop away to help others
        AutoSendCropSourceEnable,

        AutoSendCropReservePercent,

        // Auto balance resources between own villages to prevent warehouse/granary overflow.
        // A village with this on will both send away resources it's about to waste, and
        // accept resources from other villages that have room for them.
        AutoBalanceEnable,

        // Send away the surplus once a resource reaches this % of its warehouse/granary capacity.
        AutoBalanceOverflowPercent,

        // When sending, drain the resource down to this % instead of just the bare minimum,
        // so it doesn't trigger again a few minutes later.
        AutoBalanceTargetPercent,

        // Dodge: when this village comes under attack, move the chosen troop slot's full
        // stack to the nearest own village as reinforcement, then bring it back later.
        DodgeEnable,

        // Which troop slot to dodge with, 1-10 (tribe-relative order, same order shown in
        // the barracks / rally point "send troops" screen).
        DodgeTroopSlot,

        // Auto-queue smithy (weapon/armor) upgrades for one chosen troop type.
        SmithyUpgradeEnable,

        // 1-10, tribe-relative order (same order shown in the barracks/rally point).
        SmithyUpgradeTroopSlot,

        // Unix timestamp (seconds) for when the currently-running smithy research finishes.
        // Set automatically when one is detected in progress, so we don't need to revisit
        // the Smithy until it's actually over. 0 = none known.
        SmithyUpgradeBusyUntilUnixTime,

        // Auto-demolish a specific building and rebuild something else in its place.
        DemolishEnable,

        // The building's slot/location number (19-40 typically) to demolish - matches the
        // exact value Travian itself uses in the demolish dropdown, so there's no ambiguity
        // even when several buildings of the same type exist.
        DemolishSourceLocation,

        // What to build at that same location afterwards. Stored as the underlying int
        // value of BuildingEnums.
        DemolishTargetBuildingType,

        // This village will request resource top-ups from the configured hammer village
        // when it's short on wood/clay/iron/crop for its current build job.
        SupplyFromHammerEnable,

        // Fill this village's warehouse AND granary up to this % using hammer village supply.
        SupplyFromHammerTargetPercent,

        // Automatically hold a Small Celebration in the Town Hall whenever resources allow.
        SmallCelebrationEnable,

        // Unix timestamp (seconds) for when the currently-running celebration finishes.
        // Set automatically when one is detected in progress, so we don't need to revisit
        // the Town Hall until it's actually over. 0 = none known.
        SmallCelebrationBusyUntilUnixTime,

        // This village sends its own overflow (wood/clay/iron/crop above the chosen %) to
        // the configured hammer village, to speed up its troop production.
        OverflowToHammerEnable,

        // Send away the surplus once a resource reaches this % of its warehouse/granary
        // capacity in THIS (side) village.
        OverflowToHammerPercent,

        // NOTE: new members must always be appended at the end - VillageSetting.Setting is
        // stored as this enum's underlying int, so inserting/reordering/removing a member
        // would silently corrupt every existing account's stored settings (see CLAUDE.md).

        // Dodge (2026-08-13, evolved from a single-slot reinforcement into a multi-slot
        // ATTACK-type send): when this village is attacked, send the checked troop slots as
        // an attack to a fixed target coordinate shortly before impact, then cancel it within
        // Travian's 90-second cancellation window. DodgeTroopSlot (above) is no longer read by
        // this feature - kept in place only so existing enum numbering doesn't shift.
        DodgeTroopSlotsMask,

        DodgeTargetX,
        DodgeTargetY,

        // Seconds before the incoming attack lands to send the dodge movement.
        DodgeSendSecondsBeforeImpact,

        // Seconds after sending to click cancel - must stay under Travian's 90s window.
        DodgeRecallSecondsAfterSend,

        // Auto Settle (2026-08-12/13): train settlers/chieftains in this village's
        // Residence/Palace and, once 3 are ready and the 750-each founding cost is covered,
        // found a new village at a fixed target coordinate. Missing resources are pulled
        // on-demand from every other village on the account (see NeedExpansion* below and
        // SupplyForSettleTask).
        AutoSettleEnable,

        AutoSettleTargetX,
        AutoSettleTargetY,

        // % of THIS village's own warehouse/granary capacity to keep in reserve when a
        // sibling village asks for expansion resources - mirrors AutoBalanceTargetPercent's
        // role, but for the settle "on-demand pull" mechanism instead of the always-on
        // AutoBalance one.
        ExpansionSupplyReservePercent,

        // Per-resource "how much more THIS village still needs" snapshot for settler
        // training / the founding cost, written by AutoSettleTask and read by
        // SupplyForSettleTask on every other village. 0 = nothing currently needed.
        NeedExpansionWood,

        NeedExpansionClay,
        NeedExpansionIron,
        NeedExpansionCrop,

        // Bot-maintained, authoritative count of settlers currently idle/ready in THIS
        // village for Auto Settle (2026-08-18 fix). Deliberately NOT re-derived from
        // TrainTroopParser.GetPresentAmount on every run - that HTML read is an unverified
        // guess (see its own comment) and was found to misreport the count, which let
        // AutoSettleTask skip straight to founding without ever having trained settlers, and
        // separately made it keep retrying "found village" after a coordinate was already
        // settled. Instead AutoSettleTask increments this itself right after a confirmed
        // TrainSettlerCommand success (by the exact trainNow amount it just requested - no
        // parsing involved) and resets it to 0 right after a confirmed successful founding.
        AutoSettleSettlersReady,
    }
}