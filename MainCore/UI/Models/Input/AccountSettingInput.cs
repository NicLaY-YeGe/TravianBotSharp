using MainCore.UI.ViewModels.Abstract;
using MainCore.UI.ViewModels.UserControls;

namespace MainCore.UI.Models.Input
{
    public partial class AccountSettingInput : ViewModelBase
    {
        public void Set(Dictionary<AccountSettingEnums, int> settings)
        {
            Tribe.Set((TribeEnums)settings.GetValueOrDefault(AccountSettingEnums.Tribe));
            ClickDelay.Set(settings.GetValueOrDefault(AccountSettingEnums.ClickDelayMin), settings.GetValueOrDefault(AccountSettingEnums.ClickDelayMax));
            TaskDelay.Set(settings.GetValueOrDefault(AccountSettingEnums.TaskDelayMin), settings.GetValueOrDefault(AccountSettingEnums.TaskDelayMax));
            WorkTime.Set(settings.GetValueOrDefault(AccountSettingEnums.WorkTimeMin), settings.GetValueOrDefault(AccountSettingEnums.WorkTimeMax));
            SleepTime.Set(settings.GetValueOrDefault(AccountSettingEnums.SleepTimeMin), settings.GetValueOrDefault(AccountSettingEnums.SleepTimeMax));
            EnableAutoLoadVillage = settings.GetValueOrDefault(AccountSettingEnums.EnableAutoLoadVillageBuilding) == 1;
            HeadlessChrome = settings.GetValueOrDefault(AccountSettingEnums.HeadlessChrome) == 1;
            EnableAutoStartAdventure = settings.GetValueOrDefault(AccountSettingEnums.EnableAutoStartAdventure) == 1;
            EnableAutoHeroRevive = settings.GetValueOrDefault(AccountSettingEnums.EnableAutoHeroRevive) == 1;
            EnableRaidReport = settings.GetValueOrDefault(AccountSettingEnums.EnableRaidReport) == 1;
            MinHeroHealthPercent.Set(settings.GetValueOrDefault(AccountSettingEnums.MinHeroHealthPercent));
            RaidSendGap.Set(settings.GetValueOrDefault(AccountSettingEnums.RaidListSendGapMinSeconds, 30), settings.GetValueOrDefault(AccountSettingEnums.RaidListSendGapMaxSeconds, 90));
            EnableCombatCheckOnRaidList = settings.GetValueOrDefault(AccountSettingEnums.EnableCombatCheckOnRaidList) == 1;
            EnableCombatCheckOnAttack = settings.GetValueOrDefault(AccountSettingEnums.EnableCombatCheckOnAttack) == 1;
            CombatCheckMaxAgeHours.Set(settings.GetValueOrDefault(AccountSettingEnums.CombatCheckMaxAgeHours, 12));
            CombatSafetyMarginPercent.Set(settings.GetValueOrDefault(AccountSettingEnums.CombatSafetyMarginPercent, 20));
            FarmInterval.Set(settings.GetValueOrDefault(AccountSettingEnums.FarmIntervalMin), settings.GetValueOrDefault(AccountSettingEnums.FarmIntervalMax));
            UseStartAllButton = settings.GetValueOrDefault(AccountSettingEnums.UseStartAllButton) == 1;
            HammerVillageId.Set(settings.GetValueOrDefault(AccountSettingEnums.HammerVillageId));
            HammerReservePercent.Set(settings.GetValueOrDefault(AccountSettingEnums.HammerReservePercent));
            OnlineHours.Set(settings.GetValueOrDefault(AccountSettingEnums.OnlineHoursMask, AppDbContext.OnlineHoursMaskAll));
            EnableAttackWake = settings.GetValueOrDefault(AccountSettingEnums.EnableAttackWake, 1) == 1;
            AttackWakeBefore.Set(settings.GetValueOrDefault(AccountSettingEnums.AttackWakeBeforeMinMinutes, 5), settings.GetValueOrDefault(AccountSettingEnums.AttackWakeBeforeMaxMinutes, 10));
            AttackWakeAfter.Set(settings.GetValueOrDefault(AccountSettingEnums.AttackWakeAfterMinMinutes, 5), settings.GetValueOrDefault(AccountSettingEnums.AttackWakeAfterMaxMinutes, 10));
            EnableScoutAutoAttack = settings.GetValueOrDefault(AccountSettingEnums.EnableScoutAutoAttack) == 1;
            ScoutAutoAttackVillageId.Set(settings.GetValueOrDefault(AccountSettingEnums.ScoutAutoAttackVillageId));
            ScoutAutoAttackType.Set(settings.GetValueOrDefault(AccountSettingEnums.ScoutAutoAttackType));
            ScoutAutoAttackTroop1.Set(settings.GetValueOrDefault(AccountSettingEnums.ScoutAutoAttackTroop1));
            ScoutAutoAttackTroop2.Set(settings.GetValueOrDefault(AccountSettingEnums.ScoutAutoAttackTroop2));
            ScoutAutoAttackTroop3.Set(settings.GetValueOrDefault(AccountSettingEnums.ScoutAutoAttackTroop3));
            ScoutAutoAttackTroop4.Set(settings.GetValueOrDefault(AccountSettingEnums.ScoutAutoAttackTroop4));
            ScoutAutoAttackTroop5.Set(settings.GetValueOrDefault(AccountSettingEnums.ScoutAutoAttackTroop5));
            ScoutAutoAttackTroop6.Set(settings.GetValueOrDefault(AccountSettingEnums.ScoutAutoAttackTroop6));
            ScoutAutoAttackTroop7.Set(settings.GetValueOrDefault(AccountSettingEnums.ScoutAutoAttackTroop7));
            ScoutAutoAttackTroop8.Set(settings.GetValueOrDefault(AccountSettingEnums.ScoutAutoAttackTroop8));
            ScoutAutoAttackTroop9.Set(settings.GetValueOrDefault(AccountSettingEnums.ScoutAutoAttackTroop9));
            ScoutAutoAttackTroop10.Set(settings.GetValueOrDefault(AccountSettingEnums.ScoutAutoAttackTroop10));
            EnableClaimDailyQuestReward = settings.GetValueOrDefault(AccountSettingEnums.EnableClaimDailyQuestReward, 1) == 1;
        }

        public Dictionary<AccountSettingEnums, int> Get()
        {
            var tribe = (int)Tribe.Get();
            var (clickDelayMin, clickDelayMax) = ClickDelay.Get();
            var (taskDelayMin, taskDelayMax) = TaskDelay.Get();
            var isAutoLoadVillage = EnableAutoLoadVillage ? 1 : 0;
            var (workTimeMin, workTimeMax) = WorkTime.Get();
            var (sleepTimeMin, sleepTimeMax) = SleepTime.Get();
            var headlessChrome = HeadlessChrome ? 1 : 0;
            var autoStartAdventure = EnableAutoStartAdventure ? 1 : 0;
            var autoHeroRevive = EnableAutoHeroRevive ? 1 : 0;
            var raidReport = EnableRaidReport ? 1 : 0;

            var (farmIntervalMin, farmIntervalMax) = FarmInterval.Get();
            var useStartAllButton = UseStartAllButton ? 1 : 0;
            var hammerVillageId = HammerVillageId.Get();
            var hammerReservePercent = HammerReservePercent.Get();
            var onlineHoursMask = OnlineHours.Get();
            var minHeroHealthPercent = MinHeroHealthPercent.Get();
            var (raidSendGapMin, raidSendGapMax) = RaidSendGap.Get();
            var enableCombatCheckOnRaidList = EnableCombatCheckOnRaidList ? 1 : 0;
            var enableCombatCheckOnAttack = EnableCombatCheckOnAttack ? 1 : 0;
            var combatCheckMaxAgeHours = CombatCheckMaxAgeHours.Get();
            var combatSafetyMarginPercent = CombatSafetyMarginPercent.Get();
            var enableAttackWake = EnableAttackWake ? 1 : 0;
            var (attackWakeBeforeMin, attackWakeBeforeMax) = AttackWakeBefore.Get();
            var (attackWakeAfterMin, attackWakeAfterMax) = AttackWakeAfter.Get();

            var enableScoutAutoAttack = EnableScoutAutoAttack ? 1 : 0;
            var enableClaimDailyQuestReward = EnableClaimDailyQuestReward ? 1 : 0;

            var settings = new Dictionary<AccountSettingEnums, int>()
            {
                { AccountSettingEnums.ClickDelayMin, clickDelayMin },
                { AccountSettingEnums.ClickDelayMax, clickDelayMax },
                { AccountSettingEnums.TaskDelayMin, taskDelayMin },
                { AccountSettingEnums.TaskDelayMax, taskDelayMax },
                { AccountSettingEnums.EnableAutoLoadVillageBuilding, isAutoLoadVillage },

                { AccountSettingEnums.FarmIntervalMin, farmIntervalMin },
                { AccountSettingEnums.FarmIntervalMax, farmIntervalMax },
                { AccountSettingEnums.UseStartAllButton, useStartAllButton },

                { AccountSettingEnums.Tribe, tribe },
                { AccountSettingEnums.WorkTimeMax, workTimeMax },
                { AccountSettingEnums.WorkTimeMin, workTimeMin },
                { AccountSettingEnums.SleepTimeMax, sleepTimeMax },
                { AccountSettingEnums.SleepTimeMin, sleepTimeMin },

                { AccountSettingEnums.HeadlessChrome, headlessChrome },
                { AccountSettingEnums.EnableAutoStartAdventure, autoStartAdventure },
                { AccountSettingEnums.EnableAutoHeroRevive, autoHeroRevive },
                { AccountSettingEnums.EnableRaidReport, raidReport },
                { AccountSettingEnums.HammerVillageId, hammerVillageId },
                { AccountSettingEnums.HammerReservePercent, hammerReservePercent },
                { AccountSettingEnums.OnlineHoursMask, onlineHoursMask },
                { AccountSettingEnums.MinHeroHealthPercent, minHeroHealthPercent },
                { AccountSettingEnums.RaidListSendGapMinSeconds, raidSendGapMin },
                { AccountSettingEnums.RaidListSendGapMaxSeconds, raidSendGapMax },
                { AccountSettingEnums.EnableCombatCheckOnRaidList, enableCombatCheckOnRaidList },
                { AccountSettingEnums.EnableCombatCheckOnAttack, enableCombatCheckOnAttack },
                { AccountSettingEnums.CombatCheckMaxAgeHours, combatCheckMaxAgeHours },
                { AccountSettingEnums.CombatSafetyMarginPercent, combatSafetyMarginPercent },
                { AccountSettingEnums.EnableAttackWake, enableAttackWake },
                { AccountSettingEnums.AttackWakeBeforeMinMinutes, attackWakeBeforeMin },
                { AccountSettingEnums.AttackWakeBeforeMaxMinutes, attackWakeBeforeMax },
                { AccountSettingEnums.AttackWakeAfterMinMinutes, attackWakeAfterMin },
                { AccountSettingEnums.AttackWakeAfterMaxMinutes, attackWakeAfterMax },
                { AccountSettingEnums.EnableScoutAutoAttack, enableScoutAutoAttack },
                { AccountSettingEnums.ScoutAutoAttackVillageId, ScoutAutoAttackVillageId.Get() },
                { AccountSettingEnums.ScoutAutoAttackType, ScoutAutoAttackType.Get() },
                { AccountSettingEnums.ScoutAutoAttackTroop1, ScoutAutoAttackTroop1.Get() },
                { AccountSettingEnums.ScoutAutoAttackTroop2, ScoutAutoAttackTroop2.Get() },
                { AccountSettingEnums.ScoutAutoAttackTroop3, ScoutAutoAttackTroop3.Get() },
                { AccountSettingEnums.ScoutAutoAttackTroop4, ScoutAutoAttackTroop4.Get() },
                { AccountSettingEnums.ScoutAutoAttackTroop5, ScoutAutoAttackTroop5.Get() },
                { AccountSettingEnums.ScoutAutoAttackTroop6, ScoutAutoAttackTroop6.Get() },
                { AccountSettingEnums.ScoutAutoAttackTroop7, ScoutAutoAttackTroop7.Get() },
                { AccountSettingEnums.ScoutAutoAttackTroop8, ScoutAutoAttackTroop8.Get() },
                { AccountSettingEnums.ScoutAutoAttackTroop9, ScoutAutoAttackTroop9.Get() },
                { AccountSettingEnums.ScoutAutoAttackTroop10, ScoutAutoAttackTroop10.Get() },
                { AccountSettingEnums.EnableClaimDailyQuestReward, enableClaimDailyQuestReward },
            };
            return settings;
        }

        public TribeSelectorViewModel Tribe { get; } = new();
        public RangeInputViewModel ClickDelay { get; } = new();
        public RangeInputViewModel TaskDelay { get; } = new();

        public RangeInputViewModel WorkTime { get; } = new();
        public RangeInputViewModel SleepTime { get; } = new();
        public RangeInputViewModel FarmInterval { get; } = new();
        public RangeInputViewModel RaidSendGap { get; } = new();
        public RangeInputViewModel AttackWakeBefore { get; } = new();
        public RangeInputViewModel AttackWakeAfter { get; } = new();

        [Reactive]
        private bool _enableAutoLoadVillage;

        [Reactive]
        private bool _headlessChrome;

        [Reactive]
        private bool _enableAutoStartAdventure;

        [Reactive]
        private bool _enableAutoHeroRevive;

        [Reactive]
        private bool _enableRaidReport;

        [Reactive]
        private bool _enableCombatCheckOnRaidList;

        [Reactive]
        private bool _enableCombatCheckOnAttack;

        [Reactive]
        private bool _enableAttackWake;

        [Reactive]
        private bool _enableScoutAutoAttack;

        [Reactive]
        private bool _enableClaimDailyQuestReward;

        public AmountInputViewModel ScoutAutoAttackVillageId { get; } = new();
        public AmountInputViewModel ScoutAutoAttackType { get; } = new();
        public AmountInputViewModel ScoutAutoAttackTroop1 { get; } = new();
        public AmountInputViewModel ScoutAutoAttackTroop2 { get; } = new();
        public AmountInputViewModel ScoutAutoAttackTroop3 { get; } = new();
        public AmountInputViewModel ScoutAutoAttackTroop4 { get; } = new();
        public AmountInputViewModel ScoutAutoAttackTroop5 { get; } = new();
        public AmountInputViewModel ScoutAutoAttackTroop6 { get; } = new();
        public AmountInputViewModel ScoutAutoAttackTroop7 { get; } = new();
        public AmountInputViewModel ScoutAutoAttackTroop8 { get; } = new();
        public AmountInputViewModel ScoutAutoAttackTroop9 { get; } = new();
        public AmountInputViewModel ScoutAutoAttackTroop10 { get; } = new();

        public AmountInputViewModel CombatCheckMaxAgeHours { get; } = new();
        public AmountInputViewModel CombatSafetyMarginPercent { get; } = new();

        public AmountInputViewModel MinHeroHealthPercent { get; } = new();

        public AmountInputViewModel HammerVillageId { get; } = new();
        public AmountInputViewModel HammerReservePercent { get; } = new();
        public HoursScheduleViewModel OnlineHours { get; } = new();

        [Reactive]
        private bool _useStartAllButton;
    }
}