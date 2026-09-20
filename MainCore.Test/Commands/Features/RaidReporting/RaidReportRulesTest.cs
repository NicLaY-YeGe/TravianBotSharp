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
            RaidReportRules.Summarize(stats).ShouldBe("100% (full x2)");

            var low = RaidReportRules.Apply(RaidReportStats.Empty, 1, 1, 19);
            low = RaidReportRules.Apply(low, 2, 1, 3);
            RaidReportRules.Summarize(low).ShouldBe("3% (low x2)");

            RaidReportRules.Summarize(RaidReportRules.Apply(RaidReportStats.Empty, 1, 3, -1)).ShouldBe("LOST");
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
