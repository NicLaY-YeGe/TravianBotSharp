using MainCore.Commands.Features.TrainTroop;

namespace MainCore.Test.Commands.Features.TrainTroop
{
    public class ConstructionReserveRulesTest
    {
        // Phalanx-like unit: 100 / 130 / 55 / 30 per unit.
        private static readonly long[] Cost = [100, 130, 55, 30];

        [Fact]
        public void Floor_IsZeroWhenOff()
        {
            string.Join(",", ConstructionReserveRules.Floor(10000, 8000, 0, [5000, 5000, 5000, 5000], null)).ShouldBe("0,0,0,0");
        }

        [Fact]
        public void Floor_PercentOfCapacity_WarehouseForResourcesGranaryForCrop()
        {
            string.Join(",", ConstructionReserveRules.Floor(10000, 8000, 40, null, null)).ShouldBe("4000,4000,4000,3200");
        }

        [Fact]
        public void Floor_IsTheLargerOfPercentAndNextBuildingCost()
        {
            // cost beats the percent for wood and crop, the percent beats it for clay and iron
            string.Join(",", ConstructionReserveRules.Floor(10000, 8000, 40, [6000, 1000, 500, 5000], null)).ShouldBe("6000,4000,4000,5000");
        }

        [Fact]
        public void Floor_NeverAboveTheCapacity()
        {
            string.Join(",", ConstructionReserveRules.Floor(10000, 8000, 40, [12000, 0, 0, 9000], null)).ShouldBe("10000,4000,4000,8000");
            string.Join(",", ConstructionReserveRules.Floor(10000, 8000, 250, null, null)).ShouldBe("10000,10000,10000,8000");
        }

        [Fact]
        public void Floor_BusyBuildSlotRelaxesTheCostPartOnlyWhileTheQueueIsLong()
        {
            long[] cost = [6000, 6000, 6000, 6000];

            // queue runs for more than 30 minutes: only the percent holds
            string.Join(",", ConstructionReserveRules.Floor(10000, 8000, 40, cost, TimeSpan.FromMinutes(31))).ShouldBe("4000,4000,4000,3200");

            // 30 minutes or less left (or not known to be busy): the cost counts again
            string.Join(",", ConstructionReserveRules.Floor(10000, 8000, 40, cost, TimeSpan.FromMinutes(30))).ShouldBe("6000,6000,6000,6000");
            string.Join(",", ConstructionReserveRules.Floor(10000, 8000, 40, cost, TimeSpan.FromMinutes(5))).ShouldBe("6000,6000,6000,6000");
            string.Join(",", ConstructionReserveRules.Floor(10000, 8000, 40, cost, null)).ShouldBe("6000,6000,6000,6000");
        }

        [Fact]
        public void Floor_IgnoresAShortCostArray()
        {
            string.Join(",", ConstructionReserveRules.Floor(10000, 8000, 40, [100, 100], null)).ShouldBe("4000,4000,4000,3200");
        }

        [Fact]
        public void Spendable_IsStockAboveTheFloor()
        {
            var spendable = ConstructionReserveRules.Spendable([5000, 3000, 4000, 9000], [4000, 4000, 4000, 3200]);
            string.Join(",", spendable).ShouldBe("1000,0,0,5800");
        }

        [Fact]
        public void UnitsAllowed_LimitedByTheScarcestSpendableResource()
        {
            long[] floor = [4000, 4000, 4000, 3200];

            // spendable 2000 / 1300 / 1000 / 800 -> 20 / 10 / 18 / 26 units -> 10
            ConstructionReserveRules.UnitsAllowed(50, Cost, [6000, 5300, 5000, 4000], floor).ShouldBe(10);

            // never more than wanted
            ConstructionReserveRules.UnitsAllowed(5, Cost, [6000, 5300, 5000, 4000], floor).ShouldBe(5);
        }

        [Fact]
        public void UnitsAllowed_ZeroWhenTheStockIsAtOrBelowTheFloor()
        {
            ConstructionReserveRules.UnitsAllowed(10, Cost, [4000, 4000, 4000, 3200], [4000, 4000, 4000, 3200]).ShouldBe(0);
            ConstructionReserveRules.UnitsAllowed(10, Cost, [100, 100, 100, 100], [4000, 4000, 4000, 3200]).ShouldBe(0);
        }

        [Fact]
        public void UnitsAllowed_ExtraResourcesFromTheHeroAddToTheSpendableStock()
        {
            long[] floor = [4000, 4000, 4000, 3200];
            long[] stock = [4000, 4000, 4000, 3200];

            ConstructionReserveRules.UnitsAllowed(10, Cost, stock, floor, [1000, 1300, 550, 300]).ShouldBe(10);
            ConstructionReserveRules.UnitsAllowed(10, Cost, stock, floor, [500, 1300, 550, 300]).ShouldBe(5);
        }
    }
}
