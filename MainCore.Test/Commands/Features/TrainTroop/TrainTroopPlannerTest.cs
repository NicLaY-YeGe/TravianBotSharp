using MainCore.Commands.Features.Scheduling;
using MainCore.Commands.Features.TrainTroop;
using MainCore.Entities;
using MainCore.Enums;

namespace MainCore.Test.Commands.Features.TrainTroop
{
    public class TrainTroopPlannerTest
    {
        private static Func<VillageSettingEnums, int> From(Dictionary<VillageSettingEnums, int> d) => k => d.GetValueOrDefault(k);

        [Fact]
        public void SlotKeys_AreDistinct_AndAllExist()
        {
            var keys = TrainTroopSlots.AllKeys().ToList();
            Assert.Equal(40, keys.Count);
            Assert.Equal(40, keys.Distinct().Count());
        }

        [Fact]
        public void Slots_PickTroopsInTribeOrder()
        {
            var (aMin, aMax, rMin, rMax) = TrainTroopSlots.Keys(BuildingEnums.Barracks, 2);
            var settings = new Dictionary<VillageSettingEnums, int> { [aMin] = 5, [aMax] = 9, [rMin] = 10, [rMax] = 20 };
            var plans = TrainTroopPlanner.GetPlans(BuildingEnums.Barracks, TribeEnums.Gauls, From(settings));
            var plan = Assert.Single(plans);
            Assert.Equal(TroopEnums.Swordsman, plan.Troop);
            Assert.Equal((5, 9, 10, 20), (plan.AmountMin, plan.AmountMax, plan.RepeatMin, plan.RepeatMax));
        }

        [Fact]
        public void SlotBeyondTribeTroops_IsIgnored()
        {
            var (aMin, aMax, _, _) = TrainTroopSlots.Keys(BuildingEnums.Barracks, 4);
            var settings = new Dictionary<VillageSettingEnums, int> { [aMin] = 1, [aMax] = 2 };
            Assert.Empty(TrainTroopPlanner.GetPlans(BuildingEnums.Barracks, TribeEnums.Gauls, From(settings)));
        }

        [Fact]
        public void LegacySingleTroop_IsUsed_WhenNoSlotIsActive()
        {
            var settings = new Dictionary<VillageSettingEnums, int>
            {
                [VillageSettingEnums.BarrackTroop] = (int)TroopEnums.Phalanx,
                [VillageSettingEnums.BarrackAmountMin] = 10,
                [VillageSettingEnums.BarrackAmountMax] = 20,
            };
            var plan = Assert.Single(TrainTroopPlanner.GetPlans(BuildingEnums.Barracks, TribeEnums.Gauls, From(settings)));
            Assert.Equal(TroopEnums.Phalanx, plan.Troop);
            Assert.Equal(1, plan.Slot);
            Assert.Equal(20, plan.AmountMax);
        }

        [Fact]
        public void ActiveSlot_WinsOverLegacy()
        {
            var (aMin, aMax, _, _) = TrainTroopSlots.Keys(BuildingEnums.Barracks, 2);
            var settings = new Dictionary<VillageSettingEnums, int>
            {
                [VillageSettingEnums.BarrackTroop] = (int)TroopEnums.Phalanx,
                [VillageSettingEnums.BarrackAmountMax] = 20,
                [aMin] = 1,
                [aMax] = 3,
            };
            var plan = Assert.Single(TrainTroopPlanner.GetPlans(BuildingEnums.Barracks, TribeEnums.Gauls, From(settings)));
            Assert.Equal(TroopEnums.Swordsman, plan.Troop);
        }

        [Fact]
        public void GreatBarracks_UseTheSingleTroopSetting()
        {
            var settings = new Dictionary<VillageSettingEnums, int>
            {
                [VillageSettingEnums.GreatBarrackTroop] = (int)TroopEnums.Phalanx,
                [VillageSettingEnums.GreatBarrackAmountMax] = 7,
            };
            var plan = Assert.Single(TrainTroopPlanner.GetPlans(BuildingEnums.GreatBarracks, TribeEnums.Gauls, From(settings)));
            Assert.Equal(TroopEnums.Phalanx, plan.Troop);
        }

        [Fact]
        public void NothingConfigured_NoPlans()
        {
            Assert.Empty(TrainTroopPlanner.GetPlans(BuildingEnums.Workshop, TribeEnums.Romans, From(new())));
        }

        [Fact]
        public void IntervalSeconds_UsesOwnRange_ElseFallback_NeverBelowAMinute()
        {
            var own = new TroopPlan(1, TroopEnums.Phalanx, 1, 1, 5, 5);
            Assert.Equal(300, TrainTroopPlanner.IntervalSeconds(own, 120, 180, new Random(1)));
            var none = new TroopPlan(1, TroopEnums.Phalanx, 1, 1, 0, 0);
            var s = TrainTroopPlanner.IntervalSeconds(none, 120, 180, new Random(1));
            Assert.InRange(s, 7200, 10800);
            Assert.Equal(60, TrainTroopPlanner.IntervalSeconds(none, 0, 0, new Random(1)));
        }

        [Fact]
        public void PickAmount_StaysInRange_AndAtLeastOne()
        {
            var plan = new TroopPlan(1, TroopEnums.Phalanx, 3, 6, 0, 0);
            for (var i = 0; i < 50; i++) Assert.InRange(TrainTroopPlanner.PickAmount(plan, Random.Shared), 3, 6);
            Assert.Equal(1, TrainTroopPlanner.PickAmount(new TroopPlan(1, TroopEnums.Phalanx, 0, 0, 0, 0), Random.Shared));
        }

        [Fact]
        public void Schedule_IsPerTroop()
        {
            var v = new VillageId(987654);
            var t = DateTime.Now.AddHours(1);
            TrainTroopSchedule.Set(v, BuildingEnums.Barracks, t, TroopEnums.Phalanx);
            Assert.Equal(t, TrainTroopSchedule.GetNotBefore(v, BuildingEnums.Barracks, TroopEnums.Phalanx));
            Assert.Equal(DateTime.MinValue, TrainTroopSchedule.GetNotBefore(v, BuildingEnums.Barracks, TroopEnums.Swordsman));
            TrainTroopSchedule.Clear(v);
            Assert.Equal(DateTime.MinValue, TrainTroopSchedule.GetNotBefore(v, BuildingEnums.Barracks, TroopEnums.Phalanx));
        }
    }
}
