using MainCore.Commands.Features.VillageOverview;

namespace MainCore.Test.Commands.Features.VillageOverview
{
    public class VillageOverviewRulesTest
    {
        private static readonly DateTime Now = new(2026, 11, 11, 14, 0, 0);
        private static readonly string[] Names = ["Phalanx", "Swordsman", "Pathfinder", "Theutates Thunder", "Druidrider", "Haeduan", "Ram", "Trebuchet", "Chieftain", "Settler", "Hero"];

        private static int[] Counts(params int[] values)
        {
            var result = new int[11];
            for (var i = 0; i < values.Length; i++) result[i] = values[i];
            return result;
        }

        private static TroopSnapshot Snapshot(int[]? home = null, int[]? reinforcement = null, int[]? moving = null, int[]? returning = null, int[]? stationed = null, bool complete = true)
            => new(Now, Names, home ?? Counts(), reinforcement ?? Counts(), moving ?? Counts(), returning ?? Counts(), stationed ?? Counts(), complete);

        [Fact]
        public void FormatTroops_ListsOnlyTroopsAboveZero()
        {
            var text = VillageOverviewRules.FormatTroops(Names, Counts(0, 288, 0, 0, 0, 0, 0, 0, 0, 0, 1));
            text.ShouldBe("Swordsman 288, Hero 1");
        }

        [Fact]
        public void FormatTroops_NoTroopsIsADash()
        {
            VillageOverviewRules.FormatTroops(Names, Counts()).ShouldBe("-");
        }

        [Fact]
        public void FormatTroops_MissingNameFallsBackToTheSlotNumber()
        {
            VillageOverviewRules.FormatTroops([], Counts(0, 5)).ShouldBe("Unit 2 5");
        }

        [Fact]
        public void Outside_AddsMovingReturningAndStationed()
        {
            var snapshot = Snapshot(moving: Counts(0, 705), returning: Counts(0, 587), stationed: Counts(112));
            string.Join(",", VillageOverviewRules.Outside(snapshot)).ShouldBe("112,1292,0,0,0,0,0,0,0,0,0");
        }

        [Fact]
        public void OutsideText_IncompleteSnapshotIsMarkedAtLeast()
        {
            var snapshot = Snapshot(moving: Counts(0, 10), complete: false);
            VillageOverviewRules.OutsideText(snapshot).ShouldBe(">= Swordsman 10");
        }

        [Fact]
        public void OutsideText_IncompleteButEmptyHasNoMark()
        {
            VillageOverviewRules.OutsideText(Snapshot(complete: false)).ShouldBe("-");
        }

        [Fact]
        public void NoSnapshot_IsAQuestionMark()
        {
            VillageOverviewRules.HomeText(null).ShouldBe("?");
            VillageOverviewRules.OutsideText(null).ShouldBe("?");
            VillageOverviewRules.FormatMerchants(null).ShouldBe("?");
            VillageOverviewRules.FormatQueue(null, Now).ShouldBe("?");
        }

        [Fact]
        public void FormatMerchants_HomeAndAway()
        {
            VillageOverviewRules.FormatMerchants(new MerchantSnapshot(Now, 6, 10)).ShouldBe("6 home, 4 away (of 10)");
        }

        [Fact]
        public void FormatQueue_RunningAndIdle()
        {
            var running = new TrainQueueSnapshot(Now, Now.AddMinutes(202));
            VillageOverviewRules.FormatQueue(running, Now).ShouldBe("until 17:22 (202 min)");

            var ended = new TrainQueueSnapshot(Now.AddHours(-2), Now.AddMinutes(-1));
            VillageOverviewRules.FormatQueue(ended, Now).ShouldBe("idle");
        }

        [Fact]
        public void FormatBusyUntil_OffIdleAndBusy()
        {
            VillageOverviewRules.FormatBusyUntil(false, 0, Now).ShouldBe("off");
            VillageOverviewRules.FormatBusyUntil(true, 0, Now).ShouldBe("idle");

            var past = new DateTimeOffset(Now.AddMinutes(-5)).ToUnixTimeSeconds();
            VillageOverviewRules.FormatBusyUntil(true, past, Now).ShouldBe("idle");

            var future = new DateTimeOffset(Now.AddMinutes(90)).ToUnixTimeSeconds();
            VillageOverviewRules.FormatBusyUntil(true, future, Now).ShouldBe("busy until 15:30 (90 min)");
        }

        [Fact]
        public void FormatStock_AmountAndFillPercent()
        {
            VillageOverviewRules.FormatStock(8000, 16000).EndsWith("(50%)").ShouldBeTrue();
            VillageOverviewRules.FormatStock(8000, 0).Contains("%").ShouldBeFalse();
        }

        [Fact]
        public void FormatCropBalance_HasASign()
        {
            VillageOverviewRules.FormatCropBalance(123).StartsWith("+").ShouldBeTrue();
            VillageOverviewRules.FormatCropBalance(-45).StartsWith("-").ShouldBeTrue();
        }

        [Fact]
        public void FormatConstruction_RunningJobsAreListedByEndTime()
        {
            var text = VillageOverviewRules.FormatConstruction(
                [new("Warehouse", 8, Now.AddMinutes(30)), new("Granary", 7, Now.AddMinutes(12))], 0, true, Now);
            text.ShouldBe("Granary 7 until 14:12 (12 min); Warehouse 8 until 14:30 (30 min)");
        }

        [Fact]
        public void FormatConstruction_FinishedJobsCountAsIdle()
        {
            VillageOverviewRules.FormatConstruction([new("Granary", 7, Now.AddMinutes(-1))], 0, true, Now).ShouldBe("idle");
        }

        [Fact]
        public void FormatConstruction_IdleWithWaitingJobsSaysSo()
        {
            VillageOverviewRules.FormatConstruction([], 3, true, Now).ShouldBe("idle, 3 job(s) waiting");
        }

        [Fact]
        public void FormatConstruction_NeverReadVillageIsUnknown()
        {
            VillageOverviewRules.FormatConstruction([], 0, false, Now).ShouldBe("?");
        }

        [Fact]
        public void FormatConstruction_RunningJobWinsOverUnknown()
        {
            VillageOverviewRules.FormatConstruction([new("Barracks", 3, Now.AddMinutes(5))], 0, false, Now).ShouldBe("Barracks 3 until 14:05 (5 min)");
        }

        [Fact]
        public void FormatAgo_Steps()
        {
            VillageOverviewRules.FormatAgo(null, Now).ShouldBe("never");
            VillageOverviewRules.FormatAgo(Now.AddSeconds(-20), Now).ShouldBe("just now");
            VillageOverviewRules.FormatAgo(Now.AddMinutes(-5), Now).ShouldBe("5 min ago");
            VillageOverviewRules.FormatAgo(Now.AddHours(-3), Now).ShouldBe("3 h ago");
            VillageOverviewRules.FormatAgo(Now.AddDays(-3), Now).ShouldBe("3 d ago");
        }

        [Fact]
        public void Oldest_IgnoresMissingTimes()
        {
            VillageOverviewRules.Oldest([null, Now, Now.AddMinutes(-9)]).ShouldBe(Now.AddMinutes(-9));
            VillageOverviewRules.Oldest([null, null]).ShouldBeNull();
        }
    }
}
