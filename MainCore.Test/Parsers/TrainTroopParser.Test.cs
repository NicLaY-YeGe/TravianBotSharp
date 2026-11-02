using MainCore.Enums;

namespace MainCore.Test.Parsers
{
    public class TrainTroopParser : BaseParser
    {
        private const string BarrackPage = "Parsers/TrainTroop/BarrackPage.html";
        private const string StablePage = "Parsers/TrainTroop/StablePage.html";

        [Theory]
        [InlineData(BarrackPage)]
        [InlineData(StablePage)]
        public void GetTrainButton(string file)
        {
            _html.Load(file);
            var result = MainCore.Parsers.TrainTroopParser.GetTrainButton(_html);
            result.ShouldNotBeNull();
        }

        [Theory]
        [InlineData(BarrackPage, TroopEnums.Phalanx)]
        [InlineData(StablePage, TroopEnums.Pathfinder)]
        public void GetInputBox(string file, TroopEnums troop)
        {
            _html.Load(file);
            var result = MainCore.Parsers.TrainTroopParser.GetInputBox(_html, troop);
            result.ShouldNotBeNull();
        }

        [Fact]
        public void GetUnitSeconds_ReadsThePerUnitClock()
        {
            // Real capture: Phalanx "0:05:47".
            _html.Load(BarrackPage);
            MainCore.Parsers.TrainTroopParser.GetUnitSeconds(_html, TroopEnums.Phalanx).ShouldBe(347);
        }

        [Fact]
        public void GetUnitSeconds_StablePage_IsPositive()
        {
            _html.Load(StablePage);
            MainCore.Parsers.TrainTroopParser.GetUnitSeconds(_html, TroopEnums.Pathfinder).ShouldBeGreaterThan(0);
        }

        [Theory]
        [InlineData(BarrackPage, TroopEnums.Phalanx)]
        [InlineData(StablePage, TroopEnums.Pathfinder)]
        public void GetTrainTime(string file, TroopEnums troop)
        {
            _html.Load(file);
            var result = MainCore.Parsers.TrainTroopParser.GetMaxAmount(_html, troop);
            result.ShouldBeGreaterThan(-1);
        }
    }
}