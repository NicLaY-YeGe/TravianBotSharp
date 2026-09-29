namespace MainCore.Commands.Features.RaidReport
{
    // 2026-09-27, user request ("raporlara göre optimize yağma otomasyonu... karşı tarafın
    // askerlerini gördüğü kadarıyla kabaca kayıp kazanç hesabı yapacak, buna göre asker
    // gönderecek"): this had been shelved 2026-09-21 pending three scoping answers (see
    // TODO.md). Resolved: (1) every own report type feeds this - raid/attack outcomes AND
    // scouting troop reveals; (2) it applies to both Raid List raids and real Attack sends;
    // (3) with no fresh scouted garrison for a target, the bot behaves exactly as before
    // (sends without considering the garrison at all) - this class is only ever consulted when
    // a fresh ScoutedTargetGarrison row exists.
    //
    // Deliberately simple - NOT a real Travian battle simulator: no wall bonus, no hero attack
    // points, no Smithy research level on either side, no tribe/artifact bonuses, no morale, no
    // stationed-reinforcement discovery beyond what the scouting report itself revealed. Same
    // "kabaca" spirit as OasisScout/AnimalPower.cs.
    //
    // Model: our planned troops' attack points are split into an "infantry share" and a
    // "cavalry share" (CombatPower.IsCavalry decides the bucket); the target's resistance is the
    // blend of its scouted DefenseInfantry and DefenseCavalry totals, weighted by those same two
    // shares - an all-cavalry raid is measured against the defender's cavalry defense, an
    // all-infantry raid against its infantry defense, a mixed send against a proportional blend.
    public static class CombatDecisionRules
    {
        public enum Verdict { Send, Skip }

        public static long AttackPoints(IEnumerable<(TroopEnums Troop, long Count)> ourTroops)
        {
            var (infantry, cavalry) = SplitAttackPoints(ourTroops);
            return infantry + cavalry;
        }

        // (InfantryAttackPoints, CavalryAttackPoints) - used both as our total attack points and
        // to weight the defender's blended resistance in DefensePoints below.
        public static (long Infantry, long Cavalry) SplitAttackPoints(IEnumerable<(TroopEnums Troop, long Count)> ourTroops)
        {
            long infantry = 0, cavalry = 0;
            foreach (var (troop, count) in ourTroops)
            {
                if (count <= 0) continue;

                var points = count * CombatPower.Get(troop).Attack;
                if (CombatPower.IsCavalry(troop)) cavalry += points; else infantry += points;
            }
            return (infantry, cavalry);
        }

        // The target's resistance against our specific planned send, given its scouted troop
        // composition and our own attack-points split (see SplitAttackPoints). Zero troops (an
        // empty list - a real, useful "scouted, target had nothing" result, see
        // ScoutedTargetGarrison) gives zero resistance.
        public static long DefensePoints(
            IEnumerable<(TroopEnums Troop, int Count)> theirTroops,
            long ourInfantryAttackPoints,
            long ourCavalryAttackPoints)
        {
            long defInfantryTotal = 0, defCavalryTotal = 0;
            foreach (var (troop, count) in theirTroops)
            {
                if (count <= 0) continue;

                var stats = CombatPower.Get(troop);
                defInfantryTotal += (long)count * stats.DefenseInfantry;
                defCavalryTotal += (long)count * stats.DefenseCavalry;
            }

            var ourTotal = ourInfantryAttackPoints + ourCavalryAttackPoints;
            if (ourTotal <= 0) return Math.Max(defInfantryTotal, defCavalryTotal);

            var blended = (defInfantryTotal * ourInfantryAttackPoints + defCavalryTotal * ourCavalryAttackPoints)
                / (double)ourTotal;
            return (long)Math.Round(blended, MidpointRounding.AwayFromZero);
        }

        // Send only once our attack points exceed the target's (blended) defense points by at
        // least the safety margin - e.g. a 20% margin requires our attack points to reach 120%
        // of their defense points before sending; anything below that (including exact
        // break-even, 100 vs 100) skips. A target with no known defense (0 points, e.g. a
        // scouted-empty village) is always Send.
        public static Verdict Decide(long ourAttackPoints, long theirDefensePoints, int safetyMarginPercent)
        {
            if (theirDefensePoints <= 0) return Verdict.Send;

            var requiredAttackPoints = theirDefensePoints * (100 + Math.Max(0, safetyMarginPercent)) / 100;
            return ourAttackPoints >= requiredAttackPoints ? Verdict.Send : Verdict.Skip;
        }
    }
}
