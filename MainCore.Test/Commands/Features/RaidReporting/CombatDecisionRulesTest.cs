using MainCore.Commands.Features.RaidReport;
using MainCore.Enums;

namespace MainCore.Test.Commands.Features.RaidReporting
{
    public class CombatDecisionRulesTest
    {
        [Fact]
        public void SplitAttackPoints_MixedRomanSend_SplitsInfantryAndCavalry()
        {
            // 100 Legionnaire (infantry, 40 att) + 10 Equites Caesaris (cavalry, 180 att)
            var troops = new (TroopEnums, long)[]
            {
                (TroopEnums.Legionnaire, 100),
                (TroopEnums.EquitesCaesaris, 10),
            };

            var (infantry, cavalry) = CombatDecisionRules.SplitAttackPoints(troops);

            infantry.ShouldBe(4000L);
            cavalry.ShouldBe(1800L);
        }

        [Fact]
        public void DefensePoints_AllInfantryAttack_UsesOnlyDefenseInfantry()
        {
            // Attacker sends only infantry -> defender's cavalry-defense stat should not matter.
            var theirTroops = new (TroopEnums, int)[] { (TroopEnums.Praetorian, 100) }; // 65 defInf, 35 defCav

            var points = CombatDecisionRules.DefensePoints(theirTroops, ourInfantryAttackPoints: 1000, ourCavalryAttackPoints: 0);

            points.ShouldBe(6500L);
        }

        [Fact]
        public void DefensePoints_AllCavalryAttack_UsesOnlyDefenseCavalry()
        {
            var theirTroops = new (TroopEnums, int)[] { (TroopEnums.Praetorian, 100) }; // 65 defInf, 35 defCav

            var points = CombatDecisionRules.DefensePoints(theirTroops, ourInfantryAttackPoints: 0, ourCavalryAttackPoints: 1000);

            points.ShouldBe(3500L);
        }

        [Fact]
        public void DefensePoints_EmptyGarrison_IsZero()
        {
            CombatDecisionRules.DefensePoints(Array.Empty<(TroopEnums, int)>(), 500, 500).ShouldBe(0L);
        }

        [Fact]
        public void Decide_AttackClearlyStrongerThanDefense_Sends()
        {
            CombatDecisionRules.Decide(ourAttackPoints: 1000, theirDefensePoints: 500, safetyMarginPercent: 20)
                .ShouldBe(CombatDecisionRules.Verdict.Send);
        }

        [Fact]
        public void Decide_ExactBreakEven_Skips()
        {
            // 20% margin: required = 100 * 120 / 100 = 120, so exact break-even (100 vs 100) skips.
            CombatDecisionRules.Decide(ourAttackPoints: 100, theirDefensePoints: 100, safetyMarginPercent: 20)
                .ShouldBe(CombatDecisionRules.Verdict.Skip);
        }

        [Fact]
        public void Decide_AttackClearsTheMargin_Sends()
        {
            // required = 100 * 120 / 100 = 120; 120 exactly clears it.
            CombatDecisionRules.Decide(ourAttackPoints: 120, theirDefensePoints: 100, safetyMarginPercent: 20)
                .ShouldBe(CombatDecisionRules.Verdict.Send);
        }

        [Fact]
        public void Decide_DefenseClearsMargin_Skips()
        {
            // required = 100 * 120 / 100 = 120; 119 falls short.
            CombatDecisionRules.Decide(ourAttackPoints: 119, theirDefensePoints: 100, safetyMarginPercent: 20)
                .ShouldBe(CombatDecisionRules.Verdict.Skip);
        }

        [Fact]
        public void Decide_NoKnownDefense_AlwaysSends()
        {
            CombatDecisionRules.Decide(ourAttackPoints: 0, theirDefensePoints: 0, safetyMarginPercent: 20)
                .ShouldBe(CombatDecisionRules.Verdict.Send);
        }

        [Fact]
        public void CombatPower_UnrecognizedTroopType_FallsBackToUnknownNotZero()
        {
            var stats = CombatPower.Get((TroopEnums)9999);

            stats.ShouldBe(CombatPower.Unknown);
            stats.DefenseInfantry.ShouldBe(80);
        }

        [Fact]
        public void CombatPower_ScoutTypes_HaveZeroAttack()
        {
            CombatPower.Get(TroopEnums.EquitesLegati).Attack.ShouldBe(0);
            CombatPower.Get(TroopEnums.Scout).Attack.ShouldBe(0);
            CombatPower.Get(TroopEnums.Pathfinder).Attack.ShouldBe(0);
        }
    }
}
