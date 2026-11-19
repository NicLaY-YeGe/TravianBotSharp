using MainCore.Commands.Features.VillageOverview;

namespace MainCore.Test.Commands.Features.VillageOverview
{
    // The store is static: every test uses its own village ids.
    public class VillageOverviewStoreTest
    {
        private static readonly DateTime Now = new(2026, 11, 11, 14, 0, 0);

        [Fact]
        public void Merchants_AreStoredAndReturned()
        {
            VillageOverviewStore.SetMerchants(910001, 6, 10, Now);
            var snapshot = VillageOverviewStore.GetMerchants(910001)!;
            snapshot.Free.ShouldBe(6);
            snapshot.Total.ShouldBe(10);
            snapshot.ReadAt.ShouldBe(Now);
        }

        [Fact]
        public void Merchants_ImplausibleNumbersAreIgnored()
        {
            VillageOverviewStore.SetMerchants(910002, 11, 10, Now);
            VillageOverviewStore.SetMerchants(910002, 0, 0, Now);
            VillageOverviewStore.GetMerchants(910002).ShouldBeNull();
        }

        [Fact]
        public void TrainQueue_EndsAtNowPlusSeconds_AndKeepsBuildingsApart()
        {
            VillageOverviewStore.SetTrainQueue(910003, 25, 600, Now);
            VillageOverviewStore.SetTrainQueue(910003, 26, 0, Now);

            VillageOverviewStore.GetTrainQueue(910003, 25)!.EndsAt.ShouldBe(Now.AddSeconds(600));
            VillageOverviewStore.GetTrainQueue(910003, 26)!.EndsAt.ShouldBe(Now);
            VillageOverviewStore.GetTrainQueue(910003, 27).ShouldBeNull();
        }

        [Fact]
        public void Clear_RemovesEverythingOfOneVillageOnly()
        {
            VillageOverviewStore.SetMerchants(910004, 1, 2, Now);
            VillageOverviewStore.SetTrainQueue(910004, 25, 60, Now);
            VillageOverviewStore.SetMerchants(910005, 1, 2, Now);

            VillageOverviewStore.Clear(910004);

            VillageOverviewStore.GetMerchants(910004).ShouldBeNull();
            VillageOverviewStore.GetTrainQueue(910004, 25).ShouldBeNull();
            VillageOverviewStore.GetMerchants(910005).ShouldNotBeNull();
        }
    }
}
