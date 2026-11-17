using MainCore.Commands.Features.Scheduling;

namespace MainCore.Test.Commands.Features.Scheduling
{
    public class UpgradeWaitRulesTest
    {
        [Fact]
        public void IsCropProductionBlocked_RealMessage()
        {
            UpgradeWaitRules.IsCropProductionBlocked("Increase crop production.").ShouldBeTrue();
            UpgradeWaitRules.IsCropProductionBlocked("increase CROP PRODUCTION").ShouldBeTrue();
        }

        [Fact]
        public void IsCropProductionBlocked_OtherOrEmptyMessage()
        {
            UpgradeWaitRules.IsCropProductionBlocked("").ShouldBeFalse();
            UpgradeWaitRules.IsCropProductionBlocked(null).ShouldBeFalse();
            UpgradeWaitRules.IsCropProductionBlocked("Enough resources on 26.10. at 11:08.").ShouldBeFalse();
        }

        [Fact]
        public void NextWait_ReadableCountdownAlwaysWins()
        {
            UpgradeWaitRules.NextWait(TimeSpan.FromMinutes(55), true, new Random(1)).ShouldBe(TimeSpan.FromMinutes(55));
            UpgradeWaitRules.NextWait(TimeSpan.FromMinutes(55), false, new Random(1)).ShouldBe(TimeSpan.FromMinutes(55));
        }

        [Fact]
        public void NextWait_CropBlockedWithoutCountdown_IsTwoToThreeHours()
        {
            for (var i = 0; i < 100; i++)
            {
                var wait = UpgradeWaitRules.NextWait(TimeSpan.Zero, true, new Random(i));
                (wait >= TimeSpan.FromMinutes(120) && wait <= TimeSpan.FromMinutes(180)).ShouldBeTrue();
            }
        }

        [Fact]
        public void NextWait_UnknownWithoutCountdown_IsNeverZero()
        {
            for (var i = 0; i < 100; i++)
            {
                var wait = UpgradeWaitRules.NextWait(TimeSpan.Zero, false, new Random(i));
                (wait >= TimeSpan.FromMinutes(10) && wait <= TimeSpan.FromMinutes(15)).ShouldBeTrue();
            }
        }

        [Theory]
        [InlineData(1, 4, 8)]
        [InlineData(2, 8, 15)]
        [InlineData(3, 15, 30)]
        [InlineData(4, 30, 45)]
        [InlineData(20, 30, 45)]
        public void ButtonMissingWait_GrowsWithEveryMissInARow_NeverZero(int misses, int minMinutes, int maxMinutes)
        {
            for (var i = 0; i < 100; i++)
            {
                var wait = UpgradeWaitRules.ButtonMissingWait(misses, new Random(i));
                (wait >= TimeSpan.FromMinutes(minMinutes) && wait <= TimeSpan.FromMinutes(maxMinutes)).ShouldBeTrue();
            }
        }

        [Fact]
        public void ButtonMissingWait_ZeroOrNegativeCountUsesTheFirstStep()
        {
            var wait = UpgradeWaitRules.ButtonMissingWait(0, new Random(1));
            (wait >= TimeSpan.FromMinutes(4) && wait <= TimeSpan.FromMinutes(8)).ShouldBeTrue();
        }

        [Fact]
        public void ButtonMissingMemory_CountsPerVillageAndResets()
        {
            var a = new VillageId(910101);
            var b = new VillageId(910102);
            UpgradeButtonMissingMemory.Reset(a);
            UpgradeButtonMissingMemory.Reset(b);

            UpgradeButtonMissingMemory.Add(a).ShouldBe(1);
            UpgradeButtonMissingMemory.Add(a).ShouldBe(2);
            UpgradeButtonMissingMemory.Add(b).ShouldBe(1);

            UpgradeButtonMissingMemory.Reset(a);
            UpgradeButtonMissingMemory.Add(a).ShouldBe(1);
            UpgradeButtonMissingMemory.Reset(a);
            UpgradeButtonMissingMemory.Reset(b);
        }
    }
}
