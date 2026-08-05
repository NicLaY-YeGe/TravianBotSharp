namespace MainCore.Services
{
    public interface ISettingService
    {
        bool BooleanByName(AccountId accountId, AccountSettingEnums setting);

        bool BooleanByName(VillageId villageId, VillageSettingEnums setting);

        int ByName(AccountId accountId, AccountSettingEnums settingMin, AccountSettingEnums settingMax, int multiplier = 1);

        int ByName(AccountId accountId, AccountSettingEnums setting);

        Dictionary<VillageSettingEnums, int> ByName(VillageId villageId, List<VillageSettingEnums> settings);

        int ByName(VillageId villageId, VillageSettingEnums setting);

        int ByName(VillageId villageId, VillageSettingEnums settingMin, VillageSettingEnums settingMax, int multiplier = 1);

        // True if the current wall-clock hour is marked as "online" in the account's
        // OnlineHoursMask setting (see AccountSettingEnums.OnlineHoursMask).
        bool IsCurrentHourOnline(AccountId accountId);
    }
}