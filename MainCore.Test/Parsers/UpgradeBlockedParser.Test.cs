using MainCore.Enums;

namespace MainCore.Test.Parsers
{
    // Real capture (2026-10-26) of a Grain Mill contract that cannot be upgraded because crop
    // production is negative: no countdown, only the red "Increase crop production." line.
    public class UpgradeBlockedParser : BaseParser
    {
        private const string GrainMillCropBlocked = "Parsers/UpgradeBlocked/GrainMillCropBlocked.html";

        [Fact]
        public void GetBlockedMessage_CropBlocked()
        {
            _html.Load(GrainMillCropBlocked);
            var message = MainCore.Parsers.UpgradeParser.GetBlockedMessage(_html, BuildingEnums.GrainMill);
            message.ShouldBe("Increase crop production.");
        }

        [Fact]
        public void GetTimeWhenEnoughResource_CropBlockedHasNoCountdown()
        {
            _html.Load(GrainMillCropBlocked);
            var time = MainCore.Parsers.UpgradeParser.GetTimeWhenEnoughResource(_html, BuildingEnums.GrainMill);
            time.ShouldBe(TimeSpan.Zero);
        }
    }
}
