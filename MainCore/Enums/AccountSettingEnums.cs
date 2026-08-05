namespace MainCore.Enums
{
    public enum AccountSettingEnums
    {
        ClickDelayMin = 1,
        ClickDelayMax,
        TaskDelayMin,
        TaskDelayMax,
        EnableAutoLoadVillageBuilding,
        UseStartAllButton,
        FarmIntervalMin,
        FarmIntervalMax,
        Tribe,
        WorkTimeMin,
        WorkTimeMax,
        SleepTimeMin,
        SleepTimeMax,
        HeadlessChrome,
        EnableAutoStartAdventure,

        // The village that other villages can request resource top-ups from, for building
        // upgrades they're short on. 0 = none configured, otherwise a Village.Id.
        HammerVillageId,

        // Never let the hammer village's own stock drop below this % of capacity when
        // sending resources away, so troop training doesn't stall.
        HammerReservePercent,

        // Bitmask of which hours of the day (0-23, bit N = hour N) the account is allowed
        // to run tasks. Default = all 24 bits set (no restriction). Browser stays open
        // outside allowed hours, the bot just won't start a new task until an allowed
        // hour comes around.
        OnlineHoursMask,
    }
}