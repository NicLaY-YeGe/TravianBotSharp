using MainCore.UI.ViewModels.Abstract;
using MainCore.UI.ViewModels.UserControls;

namespace MainCore.UI.Models.Input
{
    public partial class VillageSettingInput : ViewModelBase
    {
        [Reactive]
        private bool _useHeroResourceForBuilding;

        [Reactive]
        private bool _applyRomanQueueLogicWhenBuilding;

        [Reactive]
        private bool _useSpecialUpgrade;

        [Reactive]
        private bool _completeImmediately;

        public TribeSelectorViewModel Tribe { get; } = new();

        [Reactive]
        private bool _trainTroopEnable;

        [Reactive]
        private bool _trainWhenLowResource;

        public TroopSelectorViewModel BarrackTroop { get; } = new();
        public TroopSelectorViewModel StableTroop { get; } = new();
        public TroopSelectorViewModel GreatBarrackTroop { get; } = new();
        public TroopSelectorViewModel GreatStableTroop { get; } = new();
        public TroopSelectorViewModel WorkshopTroop { get; } = new();

        public RangeInputViewModel TrainTroopRepeatTime { get; } = new();
        public RangeInputViewModel BarrackAmount { get; } = new();
        public RangeInputViewModel StableAmount { get; } = new();
        public RangeInputViewModel GreatBarrackAmount { get; } = new();
        public RangeInputViewModel GreatStableAmount { get; } = new();
        public RangeInputViewModel WorkshopAmount { get; } = new();

        [Reactive]
        private bool _autoNPCEnable;

        [Reactive]
        private bool _autoNPCOverflow;

        [Reactive]
        private bool _autoNPCReverse;

        public AmountInputViewModel AutoNPCGranaryPercent { get; } = new();
        public ResourceInputViewModel AutoNPCRatio { get; } = new();

        [Reactive]
        private bool _autoSendCropEnable;

        public AmountInputViewModel AutoSendCropGranaryPercent { get; } = new();

        [Reactive]
        private bool _autoSendCropSourceEnable;

        public AmountInputViewModel AutoSendCropReservePercent { get; } = new();

        [Reactive]
        private bool _autoBalanceEnable;

        public AmountInputViewModel AutoBalanceOverflowPercent { get; } = new();

        public AmountInputViewModel AutoBalanceTargetPercent { get; } = new();

        [Reactive]
        private bool _dodgeEnable;

        public AmountInputViewModel DodgeTroopSlot { get; } = new();

        [Reactive]
        private bool _smithyUpgradeEnable;

        public AmountInputViewModel SmithyUpgradeTroopSlot { get; } = new();

        [Reactive]
        private bool _demolishEnable;

        public AmountInputViewModel DemolishSourceLocation { get; } = new();

        public AmountInputViewModel DemolishTargetBuildingType { get; } = new();

        [Reactive]
        private bool _autoRefreshEnable;

        public RangeInputViewModel AutoRefreshTime { get; } = new();

        [Reactive]
        private bool _autoClaimQuestEnable;

        [Reactive]
        private int _completeImmediatelyTime;

        public void Set(Dictionary<VillageSettingEnums, int> settings)
        {
            var tribe = (TribeEnums)settings.GetValueOrDefault(VillageSettingEnums.Tribe);
            Tribe.Set(tribe);

            UseHeroResourceForBuilding = settings.GetValueOrDefault(VillageSettingEnums.UseHeroResourceForBuilding) == 1;
            ApplyRomanQueueLogicWhenBuilding = settings.GetValueOrDefault(VillageSettingEnums.ApplyRomanQueueLogicWhenBuilding) == 1;
            CompleteImmediately = settings.GetValueOrDefault(VillageSettingEnums.CompleteImmediately) == 1;
            UseSpecialUpgrade = settings.GetValueOrDefault(VillageSettingEnums.UseSpecialUpgrade) == 1;

            TrainTroopEnable = settings.GetValueOrDefault(VillageSettingEnums.TrainTroopEnable) == 1;
            TrainWhenLowResource = settings.GetValueOrDefault(VillageSettingEnums.TrainWhenLowResource) == 1;
            TrainTroopRepeatTime.Set(
                settings.GetValueOrDefault(VillageSettingEnums.TrainTroopRepeatTimeMin),
                settings.GetValueOrDefault(VillageSettingEnums.TrainTroopRepeatTimeMax));
            var barrackTroop = (TroopEnums)settings.GetValueOrDefault(VillageSettingEnums.BarrackTroop);
            BarrackTroop.Set(barrackTroop, BuildingEnums.Barracks, tribe);
            BarrackAmount.Set(
                settings.GetValueOrDefault(VillageSettingEnums.BarrackAmountMin),
                settings.GetValueOrDefault(VillageSettingEnums.BarrackAmountMax));
            var stableTroop = (TroopEnums)settings.GetValueOrDefault(VillageSettingEnums.StableTroop);
            StableTroop.Set(stableTroop, BuildingEnums.Stable, tribe);
            StableAmount.Set(
                settings.GetValueOrDefault(VillageSettingEnums.StableAmountMin),
                settings.GetValueOrDefault(VillageSettingEnums.StableAmountMax));
            var greatBarrackTroop = (TroopEnums)settings.GetValueOrDefault(VillageSettingEnums.GreatBarrackTroop);
            GreatBarrackTroop.Set(greatBarrackTroop, BuildingEnums.GreatBarracks, tribe);
            GreatBarrackAmount.Set(
                settings.GetValueOrDefault(VillageSettingEnums.GreatBarrackAmountMin),
                settings.GetValueOrDefault(VillageSettingEnums.GreatBarrackAmountMax));
            var greatStableTroop = (TroopEnums)settings.GetValueOrDefault(VillageSettingEnums.GreatStableTroop);
            GreatStableTroop.Set(greatStableTroop, BuildingEnums.GreatStable, tribe);
            GreatStableAmount.Set(
                settings.GetValueOrDefault(VillageSettingEnums.GreatStableAmountMin),
                settings.GetValueOrDefault(VillageSettingEnums.GreatStableAmountMax));
            var workshopTroop = (TroopEnums)settings.GetValueOrDefault(VillageSettingEnums.WorkshopTroop);
            WorkshopTroop.Set(workshopTroop, BuildingEnums.Workshop, tribe);
            WorkshopAmount.Set(
                settings.GetValueOrDefault(VillageSettingEnums.WorkshopAmountMin),
                settings.GetValueOrDefault(VillageSettingEnums.WorkshopAmountMax));

            AutoNPCEnable = settings.GetValueOrDefault(VillageSettingEnums.AutoNPCEnable) == 1;
            AutoNPCOverflow = settings.GetValueOrDefault(VillageSettingEnums.AutoNPCOverflow) == 1;
            AutoNPCReverse = settings.GetValueOrDefault(VillageSettingEnums.AutoNPCReverse) == 1;
            AutoNPCGranaryPercent.Set(settings.GetValueOrDefault(VillageSettingEnums.AutoNPCGranaryPercent));
            AutoNPCRatio.Set(
                settings.GetValueOrDefault(VillageSettingEnums.AutoNPCWood),
                settings.GetValueOrDefault(VillageSettingEnums.AutoNPCClay),
                settings.GetValueOrDefault(VillageSettingEnums.AutoNPCIron),
                settings.GetValueOrDefault(VillageSettingEnums.AutoNPCCrop));

            AutoSendCropEnable = settings.GetValueOrDefault(VillageSettingEnums.AutoSendCropEnable) == 1;
            AutoSendCropGranaryPercent.Set(settings.GetValueOrDefault(VillageSettingEnums.AutoSendCropGranaryPercent));
            AutoSendCropSourceEnable = settings.GetValueOrDefault(VillageSettingEnums.AutoSendCropSourceEnable) == 1;
            AutoSendCropReservePercent.Set(settings.GetValueOrDefault(VillageSettingEnums.AutoSendCropReservePercent));

            AutoBalanceEnable = settings.GetValueOrDefault(VillageSettingEnums.AutoBalanceEnable) == 1;
            AutoBalanceOverflowPercent.Set(settings.GetValueOrDefault(VillageSettingEnums.AutoBalanceOverflowPercent));
            AutoBalanceTargetPercent.Set(settings.GetValueOrDefault(VillageSettingEnums.AutoBalanceTargetPercent));

            DodgeEnable = settings.GetValueOrDefault(VillageSettingEnums.DodgeEnable) == 1;
            DodgeTroopSlot.Set(settings.GetValueOrDefault(VillageSettingEnums.DodgeTroopSlot));

            SmithyUpgradeEnable = settings.GetValueOrDefault(VillageSettingEnums.SmithyUpgradeEnable) == 1;
            SmithyUpgradeTroopSlot.Set(settings.GetValueOrDefault(VillageSettingEnums.SmithyUpgradeTroopSlot));

            DemolishEnable = settings.GetValueOrDefault(VillageSettingEnums.DemolishEnable) == 1;
            DemolishSourceLocation.Set(settings.GetValueOrDefault(VillageSettingEnums.DemolishSourceLocation));
            DemolishTargetBuildingType.Set(settings.GetValueOrDefault(VillageSettingEnums.DemolishTargetBuildingType));

            AutoRefreshEnable = settings.GetValueOrDefault(VillageSettingEnums.AutoRefreshEnable) == 1;
            AutoRefreshTime.Set(
                settings.GetValueOrDefault(VillageSettingEnums.AutoRefreshMin),
                settings.GetValueOrDefault(VillageSettingEnums.AutoRefreshMax));

            AutoClaimQuestEnable = settings.GetValueOrDefault(VillageSettingEnums.AutoClaimQuestEnable) == 1;
            CompleteImmediatelyTime = settings.GetValueOrDefault(VillageSettingEnums.CompleteImmediatelyTime);
        }

        public Dictionary<VillageSettingEnums, int> Get()
        {
            var useHeroResourceForBuilding = UseHeroResourceForBuilding ? 1 : 0;
            var applyRomanQueueLogicWhenBuilding = ApplyRomanQueueLogicWhenBuilding ? 1 : 0;
            var useSpecialUpgrade = UseSpecialUpgrade ? 1 : 0;
            var completeImmediately = CompleteImmediately ? 1 : 0;

            var tribe = (int)Tribe.Get();

            var trainTroopEnable = TrainTroopEnable ? 1 : 0;
            var trainWhenLowResource = TrainWhenLowResource ? 1 : 0;
            var (trainTroopRepeatTimeMin, trainTroopRepeatTimeMax) = TrainTroopRepeatTime.Get();
            var barrackTroop = (int)BarrackTroop.Get();
            var (barrackAmountMin, barrackAmountMax) = BarrackAmount.Get();
            var stableTroop = (int)StableTroop.Get();
            var (stableAmountMin, stableAmountMax) = StableAmount.Get();
            var greatBarrackTroop = (int)GreatBarrackTroop.Get();
            var (greatBarrackAmountMin, greatBarrackAmountMax) = GreatBarrackAmount.Get();
            var greatStableTroop = (int)GreatStableTroop.Get();
            var (greatStableAmountMin, greatStableAmountMax) = GreatStableAmount.Get();
            var workshopTroop = (int)WorkshopTroop.Get();
            var (workshopAmountMin, workshopAmountMax) = WorkshopAmount.Get();

            var autoNPCEnable = AutoNPCEnable ? 1 : 0;
            var autoNPCOverflow = AutoNPCOverflow ? 1 : 0;
            var autoNPCReverse = AutoNPCReverse ? 1 : 0;
            var autoNPCGranaryPercent = AutoNPCGranaryPercent.Get();
            var (autoNPCWood, autoNPCClay, autoNPCIron, autoNPCCrop) = AutoNPCRatio.Get();

            var autoSendCropEnable = AutoSendCropEnable ? 1 : 0;
            var autoSendCropGranaryPercent = AutoSendCropGranaryPercent.Get();
            var autoSendCropSourceEnable = AutoSendCropSourceEnable ? 1 : 0;
            var autoSendCropReservePercent = AutoSendCropReservePercent.Get();

            var autoBalanceEnable = AutoBalanceEnable ? 1 : 0;
            var autoBalanceOverflowPercent = AutoBalanceOverflowPercent.Get();
            var autoBalanceTargetPercent = AutoBalanceTargetPercent.Get();

            var dodgeEnable = DodgeEnable ? 1 : 0;
            var dodgeTroopSlot = DodgeTroopSlot.Get();

            var smithyUpgradeEnable = SmithyUpgradeEnable ? 1 : 0;
            var smithyUpgradeTroopSlot = SmithyUpgradeTroopSlot.Get();

            var demolishEnable = DemolishEnable ? 1 : 0;
            var demolishSourceLocation = DemolishSourceLocation.Get();
            var demolishTargetBuildingType = DemolishTargetBuildingType.Get();

            var autoRefreshEnable = AutoRefreshEnable ? 1 : 0;
            var (autoRefreshMin, autoRefreshMax) = AutoRefreshTime.Get();

            var autoClaimQuestEnable = AutoClaimQuestEnable ? 1 : 0;
            var completeImmediatelyTime = CompleteImmediatelyTime;

            var settings = new Dictionary<VillageSettingEnums, int>()
            {
                { VillageSettingEnums.UseHeroResourceForBuilding, useHeroResourceForBuilding },
                { VillageSettingEnums.ApplyRomanQueueLogicWhenBuilding, applyRomanQueueLogicWhenBuilding },
                { VillageSettingEnums.UseSpecialUpgrade, useSpecialUpgrade },
                { VillageSettingEnums.CompleteImmediately, completeImmediately },
                { VillageSettingEnums.Tribe, tribe },
                { VillageSettingEnums.TrainTroopEnable, trainTroopEnable },
                { VillageSettingEnums.TrainWhenLowResource, trainWhenLowResource },
                { VillageSettingEnums.TrainTroopRepeatTimeMin, trainTroopRepeatTimeMin },
                { VillageSettingEnums.TrainTroopRepeatTimeMax, trainTroopRepeatTimeMax },
                { VillageSettingEnums.BarrackTroop, barrackTroop },
                { VillageSettingEnums.BarrackAmountMin, barrackAmountMin },
                { VillageSettingEnums.BarrackAmountMax, barrackAmountMax },
                { VillageSettingEnums.StableTroop, stableTroop },
                { VillageSettingEnums.StableAmountMin, stableAmountMin },
                { VillageSettingEnums.StableAmountMax, stableAmountMax },
                { VillageSettingEnums.GreatBarrackTroop, greatBarrackTroop },
                { VillageSettingEnums.GreatBarrackAmountMin, greatBarrackAmountMin },
                { VillageSettingEnums.GreatBarrackAmountMax, greatBarrackAmountMax },
                { VillageSettingEnums.GreatStableTroop, greatStableTroop },
                { VillageSettingEnums.GreatStableAmountMin, greatStableAmountMin },
                { VillageSettingEnums.GreatStableAmountMax, greatStableAmountMax },
                { VillageSettingEnums.WorkshopTroop, workshopTroop },
                { VillageSettingEnums.WorkshopAmountMin, workshopAmountMin },
                { VillageSettingEnums.WorkshopAmountMax, workshopAmountMax },
                { VillageSettingEnums.AutoNPCEnable, autoNPCEnable },
                { VillageSettingEnums.AutoNPCOverflow, autoNPCOverflow },
                { VillageSettingEnums.AutoNPCReverse, autoNPCReverse },
                { VillageSettingEnums.AutoNPCGranaryPercent, autoNPCGranaryPercent },
                { VillageSettingEnums.AutoNPCWood, autoNPCWood },
                { VillageSettingEnums.AutoNPCClay, autoNPCClay },
                { VillageSettingEnums.AutoNPCIron, autoNPCIron },
                { VillageSettingEnums.AutoNPCCrop, autoNPCCrop },
                { VillageSettingEnums.AutoSendCropEnable, autoSendCropEnable },
                { VillageSettingEnums.AutoSendCropGranaryPercent, autoSendCropGranaryPercent },
                { VillageSettingEnums.AutoSendCropSourceEnable, autoSendCropSourceEnable },
                { VillageSettingEnums.AutoSendCropReservePercent, autoSendCropReservePercent },

                { VillageSettingEnums.AutoBalanceEnable, autoBalanceEnable },
                { VillageSettingEnums.AutoBalanceOverflowPercent, autoBalanceOverflowPercent },
                { VillageSettingEnums.AutoBalanceTargetPercent, autoBalanceTargetPercent },

                { VillageSettingEnums.DodgeEnable, dodgeEnable },
                { VillageSettingEnums.DodgeTroopSlot, dodgeTroopSlot },

                { VillageSettingEnums.SmithyUpgradeEnable, smithyUpgradeEnable },
                { VillageSettingEnums.SmithyUpgradeTroopSlot, smithyUpgradeTroopSlot },

                { VillageSettingEnums.DemolishEnable, demolishEnable },
                { VillageSettingEnums.DemolishSourceLocation, demolishSourceLocation },
                { VillageSettingEnums.DemolishTargetBuildingType, demolishTargetBuildingType },
                { VillageSettingEnums.AutoRefreshEnable, autoRefreshEnable },
                { VillageSettingEnums.AutoRefreshMin, autoRefreshMin },
                { VillageSettingEnums.AutoRefreshMax, autoRefreshMax },
                { VillageSettingEnums.AutoClaimQuestEnable, autoClaimQuestEnable },
                { VillageSettingEnums.CompleteImmediatelyTime, completeImmediatelyTime },
            };
            return settings;
        }

        public VillageSettingInput()
        {
            this.WhenAnyValue(vm => vm.Tribe.SelectedItem)
                .Select(x => x.Tribe)
                .Subscribe((tribe) =>
                {
                    BarrackTroop.ChangeTribe(BuildingEnums.Barracks, tribe);
                    StableTroop.ChangeTribe(BuildingEnums.Stable, tribe);
                    GreatBarrackTroop.ChangeTribe(BuildingEnums.GreatBarracks, tribe);
                    GreatStableTroop.ChangeTribe(BuildingEnums.GreatStable, tribe);
                    WorkshopTroop.ChangeTribe(BuildingEnums.Workshop, tribe);
                });
        }
    }
}