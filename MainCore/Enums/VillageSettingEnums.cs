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
    }
}