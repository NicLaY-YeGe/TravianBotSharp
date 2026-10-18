namespace MainCore.Test.Parsers
{
    public class RallyPointOverviewParser : BaseParser
    {
        private const string IncomingAttackPage = "Parsers/RallyPointOverview/IncomingAttack.html";

        // Real capture (2026-10-18): the timer seconds live in `value`, not `data-value`.
        [Fact]
        public void GetIncomingAttackSeconds_ReadsValueAttribute()
        {
            _html.Load(IncomingAttackPage);
            var actual = MainCore.Parsers.RallyPointOverviewParser.GetIncomingAttackSeconds(_html);
            actual.ShouldBe(424);
        }
    }
}
