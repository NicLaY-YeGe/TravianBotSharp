using MainCore.Commands.Features.RaidReport;
using MainCore.Entities;

namespace MainCore.Test.Commands.Features.RaidReporting
{
    public class RaidReportRulesTest
    {
        [Theory]
        [InlineData(42, 225, 19)]
        [InlineData(13, 455, 3)]
        [InlineData(169, 180, 94)]
        [InlineData(325, 325, 100)]
        [InlineData(400, 300, 100)]   // never above 100
        [InlineData(0, 90, 0)]
        [InlineData(0, 0, -1)]        // nothing could be carried (hero-only raid) = unknown
        public void LootPercent_RoundsAndClamps(int carried, int capacity, int expected)
        {
            RaidReportRules.LootPercent(carried, capacity).ShouldBe(expected);
        }

        [Fact]
        public void Apply_CleanReports_TrackLowAndFullLootStreaks()
        {
            var stats = RaidReportStats.Empty;

            stats = RaidReportRules.Apply(stats, 1, 1, 19);
            stats = RaidReportRules.Apply(stats, 2, 1, 3);
            stats.LowLootStreak.ShouldBe(2);
            stats.FullLootStreak.ShouldBe(0);
            stats.ConsecutiveLossy.ShouldBe(0);
            stats.ReportCount.ShouldBe(2);

            stats = RaidReportRules.Apply(stats, 3, 1, 100);
            stats.LowLootStreak.ShouldBe(0);
            stats.FullLootStreak.ShouldBe(1);

            stats = RaidReportRules.Apply(stats, 4, 1, 100);
            stats.FullLootStreak.ShouldBe(2);

            stats = RaidReportRules.Apply(stats, 5, 1, 60);
            stats.FullLootStreak.ShouldBe(0);
            stats.LowLootStreak.ShouldBe(0);
        }

        [Fact]
        public void Apply_UnknownLoot_LeavesTheStreaksAlone()
        {
            var stats = RaidReportRules.Apply(RaidReportStats.Empty, 1, 1, 10);
            stats = RaidReportRules.Apply(stats, 2, 1, -1);

            stats.LowLootStreak.ShouldBe(1);
            stats.LastLootPercent.ShouldBe(-1);
            stats.ReportCount.ShouldBe(2);
        }

        [Fact]
        public void Apply_SameOrOlderReport_IsIgnored()
        {
            var stats = RaidReportRules.Apply(RaidReportStats.Empty, 10, 1, 50);

            RaidReportRules.Apply(stats, 10, 3, 0).ShouldBeSameAs(stats);
            RaidReportRules.Apply(stats, 9, 3, 0).ShouldBeSameAs(stats);
        }

        [Fact]
        public void Apply_LossyReports_CountInARowAndAreResetByACleanOne()
        {
            var stats = RaidReportRules.Apply(RaidReportStats.Empty, 1, 2, 40);
            stats.ConsecutiveLossy.ShouldBe(1);

            stats = RaidReportRules.Apply(stats, 2, 2, 40);
            stats.ConsecutiveLossy.ShouldBe(2);

            stats = RaidReportRules.Apply(stats, 3, 1, 40);
            stats.ConsecutiveLossy.ShouldBe(0);
        }

        [Fact]
        public void GetPauseReason_LostRaid_PausesAtOnce()
        {
            var stats = RaidReportRules.Apply(RaidReportStats.Empty, 1, 3, -1);

            RaidReportRules.GetPauseReason(stats).ShouldNotBeNull();
        }

        [Fact]
        public void GetPauseReason_OneLossyRaid_DoesNotPause_TwoInARowDo()
        {
            var stats = RaidReportRules.Apply(RaidReportStats.Empty, 1, 2, 40);
            RaidReportRules.GetPauseReason(stats).ShouldBeNull();

            stats = RaidReportRules.Apply(stats, 2, 2, 40);
            RaidReportRules.GetPauseReason(stats).ShouldNotBeNull();
        }

        [Fact]
        public void GetPauseReason_LowOrFullLoot_NeverPausesByItself()
        {
            // The first real list had 5 of 8 raids under 30% - none of that may pause a row.
            var stats = RaidReportStats.Empty;
            for (var i = 1; i <= 10; i++) stats = RaidReportRules.Apply(stats, i, 1, 3);
            RaidReportRules.GetPauseReason(stats).ShouldBeNull();

            for (var i = 11; i <= 20; i++) stats = RaidReportRules.Apply(stats, i, 1, 100);
            RaidReportRules.GetPauseReason(stats).ShouldBeNull();
        }

        [Fact]
        public void Summarize_ShowsLastResultAndNotableStreaks()
        {
            RaidReportRules.Summarize(RaidReportStats.Empty).ShouldBe("");

            var stats = RaidReportRules.Apply(RaidReportStats.Empty, 1, 1, 94);
            RaidReportRules.Summarize(stats).ShouldBe("94%");

            stats = RaidReportRules.Apply(stats, 2, 1, 100);
            stats = RaidReportRules.Apply(stats, 3, 1, 100);
            RaidReportRules.Summarize(stats).ShouldBe("100% (full x2, troops 125%)"); // FullLootStreakToGrow just hit

            var low = RaidReportRules.Apply(RaidReportStats.Empty, 1, 1, 19);
            low = RaidReportRules.Apply(low, 2, 1, 3);
            RaidReportRules.Summarize(low).ShouldBe("3% (low x2)");

            RaidReportRules.Summarize(RaidReportRules.Apply(RaidReportStats.Empty, 1, 3, -1)).ShouldBe("LOST");

            var shrunk = RaidReportStats.Empty;
            shrunk = RaidReportRules.Apply(shrunk, 1, 1, 10);
            shrunk = RaidReportRules.Apply(shrunk, 2, 1, 10);
            shrunk = RaidReportRules.Apply(shrunk, 3, 1, 10);
            RaidReportRules.Summarize(shrunk).ShouldBe("10% (low x3, troops 80%)");
        }

        [Fact]
        public void MatchEntries_ByTarget_AndOptionallyBySourceVillage()
        {
            var a1 = new RaidListEntry { Id = 1, VillageId = 10, TargetX = 5, TargetY = -6 };
            var b1 = new RaidListEntry { Id = 2, VillageId = 20, TargetX = 5, TargetY = -6 };
            var a2 = new RaidListEntry { Id = 3, VillageId = 10, TargetX = 7, TargetY = 8 };
            var all = new[] { a1, b1, a2 };

            string.Join(",", RaidReportRules.MatchEntries(all, 5, -6, null).Select(e => e.Id).OrderBy(id => id)).ShouldBe("1,2");
            string.Join(",", RaidReportRules.MatchEntries(all, 5, -6, 20).Select(e => e.Id)).ShouldBe("2");
            RaidReportRules.MatchEntries(all, 9, 9, null).Count.ShouldBe(0);

            RaidReportRules.SpansSeveralVillages(new[] { a1, b1 }).ShouldBeTrue();
            RaidReportRules.SpansSeveralVillages(new[] { a1, a2 }).ShouldBeFalse();
        }

        [Fact]
        public void ResolveAttackerVillage_PicksTheVillageWhoseCoordinatesFitTheTileId()
        {
            var villages = new (int VillageId, int X, int Y)[]
            {
                (71146, 144, -82),
                (71147, -10, 30),
            };

            var resolved = RaidReportRules.ResolveAttackerVillage(113427, villages);

            resolved.ShouldNotBeNull();
            resolved.Value.VillageId.ShouldBe(71146);
            resolved.Value.Radius.ShouldBe(200);
        }

        [Fact]
        public void ResolveAttackerVillage_NoVillageFits_ReturnsNull()
        {
            var villages = new (int VillageId, int X, int Y)[] { (1, 0, 0) };

            RaidReportRules.ResolveAttackerVillage(113427, villages).ShouldBeNull();
        }

        [Fact]
        public void ResolveAttackerVillage_TargetOfARealReport_DecodesThroughTheInferredRadius()
        {
            // End to end with the real numbers: own village (144|-82) = d 113427 gives radius 200,
            // and the defender's d 114225 then decodes to (140|-84).
            var attacker = RaidReportRules.ResolveAttackerVillage(113427, new (int VillageId, int X, int Y)[] { (1, 144, -82) });

            attacker.ShouldNotBeNull();
            MapTiles.ToCoordinates(114225, attacker.Value.Radius).ShouldBe((140, -84));
        }

        [Fact]
        public void Apply_ThreeLowLootReportsInARow_ShrinksTheMultiplierOnce()
        {
            var stats = RaidReportStats.Empty;
            stats.EffectiveTroopMultiplierPercent.ShouldBe(100);

            stats = RaidReportRules.Apply(stats, 1, 1, 10);
            stats = RaidReportRules.Apply(stats, 2, 1, 5);
            stats.TroopMultiplierPercent.ShouldBe(100); // streak is only 2 so far

            stats = RaidReportRules.Apply(stats, 3, 1, 8); // streak hits 3 -> one shrink step
            stats.LowLootStreak.ShouldBe(3);
            stats.TroopMultiplierPercent.ShouldBe(80);

            // Streak keeps running past 3 - must NOT shrink again every report.
            stats = RaidReportRules.Apply(stats, 4, 1, 12);
            stats.LowLootStreak.ShouldBe(4);
            stats.TroopMultiplierPercent.ShouldBe(80);
        }

        [Fact]
        public void Apply_TwoFullLootReportsInARow_GrowsTheMultiplierOnce()
        {
            var stats = RaidReportStats.Empty;

            stats = RaidReportRules.Apply(stats, 1, 1, 100);
            stats.TroopMultiplierPercent.ShouldBe(100); // streak is only 1

            stats = RaidReportRules.Apply(stats, 2, 1, 100); // streak hits 2 -> one grow step
            stats.FullLootStreak.ShouldBe(2);
            stats.TroopMultiplierPercent.ShouldBe(125);

            stats = RaidReportRules.Apply(stats, 3, 1, 100);
            stats.FullLootStreak.ShouldBe(3);
            stats.TroopMultiplierPercent.ShouldBe(125); // no second step just from staying full
        }

        [Fact]
        public void Apply_AResetStreakDoesNotUndoAMultiplierStepAlreadyTaken()
        {
            var stats = RaidReportStats.Empty;
            stats = RaidReportRules.Apply(stats, 1, 1, 10);
            stats = RaidReportRules.Apply(stats, 2, 1, 10);
            stats = RaidReportRules.Apply(stats, 3, 1, 10); // shrink to 80
            stats.TroopMultiplierPercent.ShouldBe(80);

            stats = RaidReportRules.Apply(stats, 4, 1, 60); // breaks the low-loot streak
            stats.LowLootStreak.ShouldBe(0);
            stats.TroopMultiplierPercent.ShouldBe(80); // stays shrunk, not un-done
        }

        [Theory]
        [InlineData(100, 3, 0, 80)]
        [InlineData(50, 3, 0, 40)]     // 50*0.8=40 - lands exactly on the floor
        [InlineData(45, 3, 0, 40)]     // 45*0.8=36 - clamped UP to the floor
        [InlineData(100, 0, 2, 125)]
        [InlineData(180, 0, 2, 200)]   // clamped to the ceiling
        [InlineData(100, 0, 0, 100)]   // neither streak at its threshold - unchanged
        [InlineData(100, 2, 0, 100)]   // streak short of threshold - unchanged
        [InlineData(100, 6, 0, 100)]   // streak already past threshold (no == match) - unchanged
        public void NextTroopMultiplierPercent_MovesOneStepAtThresholdAndClamps(
            int current, int lowLootStreak, int fullLootStreak, int expected)
        {
            RaidReportRules.NextTroopMultiplierPercent(current, lowLootStreak, fullLootStreak).ShouldBe(expected);
        }

        [Fact]
        public void EffectiveTroopMultiplierPercent_TreatsAMissingOrZeroValueAs100()
        {
            // A row's stored JSON from before this field existed deserializes with
            // TroopMultiplierPercent==0 (System.Text.Json's constructor matching fills a
            // missing property with the C# default, not the record's own 100 default) -
            // EffectiveTroopMultiplierPercent must read that as "unchanged", not "send 0".
            var beforeThisFeature = RaidReportStats.Empty with { TroopMultiplierPercent = 0 };

            beforeThisFeature.EffectiveTroopMultiplierPercent.ShouldBe(100);
            RaidReportStats.Empty.EffectiveTroopMultiplierPercent.ShouldBe(100);
        }

        [Theory]
        [InlineData(2, 6, 100, 2, 6)]
        [InlineData(10, 20, 80, 8, 16)]
        [InlineData(10, 20, 125, 12, 25)]
        [InlineData(1, 1, 40, 1, 1)]     // never scales a positive amount down to 0
        [InlineData(0, 0, 40, 0, 0)]     // an unused slot (0/0) stays 0
        [InlineData(3, 3, 40, 1, 1)]     // 3*0.4=1.2 -> floored to 1, Min still equals Max
        public void ScaleRange_AppliesThePercentAndNeverZerosARealAmount(
            long min, long max, int multiplierPercent, long expectedMin, long expectedMax)
        {
            var scaled = RaidReportRules.ScaleRange(new TroopAmountRange(min, max), multiplierPercent);

            scaled.Min.ShouldBe(expectedMin);
            scaled.Max.ShouldBe(expectedMax);
        }

        [Fact]
        public void ReportStats_RoundTripThroughTheEntryJsonColumn()
        {
            var entry = new RaidListEntry();
            entry.GetReportStats().ShouldBe(RaidReportStats.Empty);

            var stats = RaidReportRules.Apply(RaidReportStats.Empty, 19345928, 1, 94);
            entry.SetReportStats(stats);

            entry.GetReportStats().ShouldBe(stats);

            entry.ReportStatsJson = "not json";
            entry.GetReportStats().ShouldBe(RaidReportStats.Empty);
        }
    }
}
