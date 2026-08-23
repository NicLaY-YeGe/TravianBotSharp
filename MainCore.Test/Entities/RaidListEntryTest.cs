using MainCore.Entities;

namespace MainCore.Test.Entities
{
    public class RaidListEntryTest
    {
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
