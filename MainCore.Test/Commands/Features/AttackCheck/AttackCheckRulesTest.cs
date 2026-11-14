using MainCore.Commands.Features.AttackCheck;

namespace MainCore.Test.Commands.Features.AttackCheck
{
    public class AttackCheckRulesTest
    {
        [Fact]
        public void NextDelay_IsAlwaysBetweenFourAndSixMinutes()
        {
            for (var i = 0; i < 500; i++)
            {
                var seconds = AttackCheckRules.NextDelay(new Random(i)).TotalSeconds;
                (seconds >= 240 && seconds <= 360).ShouldBeTrue();
            }
        }

        [Fact]
        public void NextDelay_IsNotAFixedClock()
        {
            var values = Enumerable.Range(0, 50).Select(i => AttackCheckRules.NextDelay(new Random(i)).TotalSeconds).Distinct().Count();
            (values > 10).ShouldBeTrue();
        }
    }
}
