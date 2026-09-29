using MainCore.Entities;
using MainCore.Enums;

namespace MainCore.Test.Entities
{
    public class ScoutedTargetGarrisonTest
    {
        [Fact]
        public void SetTroops_ThenGetTroops_RoundTrips()
        {
            var garrison = new ScoutedTargetGarrison();
            var troops = new (TroopEnums, int)[]
            {
                (TroopEnums.Clubswinger, 50),
                (TroopEnums.Spearman, 25),
            };

            garrison.SetTroops(troops);
            var result = garrison.GetTroops();

            string.Join(",", result.Select(x => $"{x.Troop}:{x.Count}")).ShouldBe("Clubswinger:50,Spearman:25");
        }

        [Fact]
        public void GetTroops_EmptyJson_ReturnsEmptyList()
        {
            var garrison = new ScoutedTargetGarrison();
            garrison.SetTroops(Array.Empty<(TroopEnums, int)>());

            garrison.GetTroops().ShouldBeEmpty();
        }

        [Fact]
        public void GetTroops_NullOrBlankJson_ReturnsEmptyListRatherThanThrowing()
        {
            var garrison = new ScoutedTargetGarrison { TroopsJson = null };
            garrison.GetTroops().ShouldBeEmpty();

            garrison.TroopsJson = "";
            garrison.GetTroops().ShouldBeEmpty();
        }

        [Fact]
        public void GetTroops_CorruptJson_ReturnsEmptyListRatherThanThrowing()
        {
            var garrison = new ScoutedTargetGarrison { TroopsJson = "{not valid json" };
            garrison.GetTroops().ShouldBeEmpty();
        }
    }
}
