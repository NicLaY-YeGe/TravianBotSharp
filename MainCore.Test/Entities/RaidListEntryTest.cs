using MainCore.Commands.Features.RaidReport;
using MainCore.Entities;

namespace MainCore.Test.Entities
{
    public class RaidListEntryTest
    {
        [Fact]
        public void RollTroopAmounts_NoReportStatsYet_UsesTheConfiguredRangeUnscaled()
        {
            var entry = new RaidListEntry();
            entry.SetTroopAmountRanges(new Dictionary<int, TroopAmountRange> { [2] = new TroopAmountRange(10, 10) });

            entry.RollTroopAmounts(Random.Shared)[2].ShouldBe(10);
        }

        [Fact]
        public void RollTroopAmounts_ShrunkMultiplier_ScalesEverySlotDown()
        {
            var entry = new RaidListEntry();
            entry.SetTroopAmountRanges(new Dictionary<int, TroopAmountRange> { [2] = new TroopAmountRange(10, 10) });

            var stats = RaidReportStats.Empty;
            stats = RaidReportRules.Apply(stats, 1, 1, 10);
            stats = RaidReportRules.Apply(stats, 2, 1, 10);
            stats = RaidReportRules.Apply(stats, 3, 1, 10); // three low-loot reports -> x0.8
            entry.SetReportStats(stats);
            stats.TroopMultiplierPercent.ShouldBe(80);

            entry.RollTroopAmounts(Random.Shared)[2].ShouldBe(8);
        }

        [Fact]
        public void RollTroopAmounts_GrownMultiplier_ScalesEverySlotUp()
        {
            var entry = new RaidListEntry();
            entry.SetTroopAmountRanges(new Dictionary<int, TroopAmountRange> { [2] = new TroopAmountRange(10, 10) });

            var stats = RaidReportStats.Empty;
            stats = RaidReportRules.Apply(stats, 1, 1, 100);
            stats = RaidReportRules.Apply(stats, 2, 1, 100); // two full-loot reports -> x1.25
            entry.SetReportStats(stats);

            entry.RollTroopAmounts(Random.Shared)[2].ShouldBe(12);
        }

        [Fact]
        public void RollTroopAmounts_ConfiguredMinMaxInTheDatabaseIsNeverRewritten()
        {
            var entry = new RaidListEntry();
            entry.SetTroopAmountRanges(new Dictionary<int, TroopAmountRange> { [2] = new TroopAmountRange(10, 10) });
            entry.SetReportStats(RaidReportStats.Empty with { TroopMultiplierPercent = 80 });

            entry.RollTroopAmounts(Random.Shared);

            entry.GetTroopAmountRanges()[2].Min.ShouldBe(10); // the scaling only affects the roll, not storage
            entry.GetTroopAmountRanges()[2].Max.ShouldBe(10);
        }

        [Fact]
        public void RollTroopAmounts_StaysWithinMinMax_AcrossManyRolls()
        {
            var entry = new RaidListEntry();
            entry.SetTroopAmountRanges(new Dictionary<int, TroopAmountRange>
            {
                [2] = new TroopAmountRange(2, 6),
            });

            for (var i = 0; i < 200; i++)
            {
                var amounts = entry.RollTroopAmounts(Random.Shared);
                amounts[2].ShouldBeInRange(2, 6);
            }
        }

        [Fact]
        public void RollTroopAmounts_MinEqualsMax_AlwaysReturnsThatAmount()
        {
            var entry = new RaidListEntry();
            entry.SetTroopAmountRanges(new Dictionary<int, TroopAmountRange>
            {
                [2] = new TroopAmountRange(5, 5),
            });

            entry.RollTroopAmounts(Random.Shared)[2].ShouldBe(5);
        }

        [Fact]
        public void GetTroopAmountRanges_FallsBackToOldFixedAmountColumn_WhenRangesColumnIsEmpty()
        {
            // Rows created before 2026-08-22 only have TroopAmountsJson (fixed dict) -
            // TroopAmountRangesJson is null/empty for them until edited. See the "Superseded
            // by TroopAmountRangesJson" comment on RaidListEntry.TroopAmountsJson.
            var entry = new RaidListEntry();
            entry.SetTroopAmounts(new Dictionary<int, long> { [2] = 68 });

            var ranges = entry.GetTroopAmountRanges();

            ranges[2].Min.ShouldBe(68);
            ranges[2].Max.ShouldBe(68);
        }

        [Fact]
        public void GetTroopAmountRanges_PrefersNewRangesColumn_OverOldFixedAmountColumn()
        {
            var entry = new RaidListEntry();
            entry.SetTroopAmounts(new Dictionary<int, long> { [2] = 68 });
            entry.SetTroopAmountRanges(new Dictionary<int, TroopAmountRange> { [2] = new TroopAmountRange(2, 6) });

            var ranges = entry.GetTroopAmountRanges();

            ranges[2].Min.ShouldBe(2);
            ranges[2].Max.ShouldBe(6);
        }
    }
}
