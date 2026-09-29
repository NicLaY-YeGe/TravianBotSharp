namespace MainCore.Commands.Features.RaidReport
{
    // Rough (not simulator-accurate) attack / defense-vs-infantry / defense-vs-cavalry point
    // table for real (player) troop types, used by CombatDecisionRules to roughly compare our
    // planned send against a target's most recently scouted garrison. Same "kaba tahmin" spirit
    // as OasisScout/AnimalPower.cs: no wall bonus, no hero attack points, no Smithy research
    // level, no tribe/artifact bonuses, no morale - just raw published base (unresearched)
    // stats. User-approved framing (2026-09-27): "kabaca kayıp kazanç hesabı" - a rough call,
    // not a real battle outcome prediction.
    //
    // Confidence, by tribe:
    //  - Romans / Teutons / Gauls: taken directly from a public Travian stats reference
    //    (travian.fandom.com/wiki/Troops, checked 2026-09-27) - high confidence.
    //  - Egyptians / Huns: that source only publishes "value per crop upkeep" ratios for these
    //    two tribes, not the raw values, so these were back-calculated (ratio x an assumed
    //    upkeep, inferred from each troop's usual slot - infantry upkeep 1, light cavalry 2,
    //    heavy cavalry/chariot 3) and cross-checked only where an independently published exact
    //    number existed (Slave Militia, Ash Warden - both matched exactly, including Ash
    //    Warden's published "95 total defense per crop" = 55 + 40). Everything else in these two
    //    tribes - and especially Marksman, Marauder, Sopdu Explorer, Spotter, and every
    //    Egyptian/Hun ram/catapult/chief - is a LOWER-CONFIDENCE estimate. Verify against the
    //    in-game unit info tooltip (hover a troop icon in the Rally Point) before relying on
    //    this feature for an Egyptian or Hun account, and tell the next session if a number here
    //    turns out to be wrong so it can be corrected.
    //  - Natars and Nature are NOT covered here - Nature already has its own table
    //    (OasisScout/AnimalPower.cs) for the unrelated oasis-vs-hero decision, and Natar
    //    villages are not a normal raid/attack target for this bot. Any troop type this table
    //    has no entry for - a future new unit, a parsing miss, or one of those two - falls back
    //    to Unknown below.
    public static class CombatPower
    {
        public readonly record struct Stats(int Attack, int DefenseInfantry, int DefenseCavalry);

        // A troop type this table doesn't recognize is treated as a moderate, NOT undefended,
        // garrison - silently assuming "empty" for an unknown troop type would make
        // CombatDecisionRules recommend sending into an unscouted-for-real garrison as if it
        // were harmless, which is the unsafe direction to guess wrong in. 80/80/80 sits roughly
        // at "Settler-tier" defense - deliberately unremarkable rather than alarmist.
        public static readonly Stats Unknown = new(80, 80, 80);

        private static readonly IReadOnlyDictionary<TroopEnums, Stats> Table = new Dictionary<TroopEnums, Stats>
        {
            // Romans (confirmed)
            [TroopEnums.Legionnaire] = new(40, 35, 50),
            [TroopEnums.Praetorian] = new(30, 65, 35),
            [TroopEnums.Imperian] = new(70, 40, 25),
            [TroopEnums.EquitesLegati] = new(0, 20, 10),
            [TroopEnums.EquitesImperatoris] = new(120, 65, 50),
            [TroopEnums.EquitesCaesaris] = new(180, 80, 105),
            [TroopEnums.RomanRam] = new(60, 30, 75),
            [TroopEnums.RomanCatapult] = new(75, 60, 10),
            [TroopEnums.RomanChief] = new(50, 40, 30),
            [TroopEnums.RomanSettler] = new(0, 80, 80),

            // Teutons (confirmed)
            [TroopEnums.Clubswinger] = new(40, 20, 5),
            [TroopEnums.Spearman] = new(10, 35, 60),
            [TroopEnums.Axeman] = new(60, 30, 30),
            [TroopEnums.Scout] = new(0, 10, 5),
            [TroopEnums.Paladin] = new(55, 100, 40),
            [TroopEnums.TeutonicKnight] = new(150, 50, 75),
            [TroopEnums.TeutonRam] = new(65, 30, 80),
            [TroopEnums.TeutonCatapult] = new(50, 60, 10),
            [TroopEnums.TeutonChief] = new(40, 60, 40),
            [TroopEnums.TeutonSettler] = new(10, 80, 80),

            // Gauls (confirmed)
            [TroopEnums.Phalanx] = new(15, 40, 50),
            [TroopEnums.Swordsman] = new(65, 35, 20),
            [TroopEnums.Pathfinder] = new(0, 20, 10),
            [TroopEnums.TheutatesThunder] = new(100, 25, 40),
            [TroopEnums.Druidrider] = new(45, 115, 55),
            [TroopEnums.Haeduan] = new(140, 50, 165),
            [TroopEnums.GaulRam] = new(50, 30, 105),
            [TroopEnums.GaulCatapult] = new(70, 45, 10),
            [TroopEnums.GaulChief] = new(40, 50, 50),
            [TroopEnums.GaulSettler] = new(0, 80, 80),

            // Egyptians (back-calculated - see class comment; Slave Militia/Ash Warden cross-
            // checked and exact, the rest lower confidence)
            [TroopEnums.SlaveMilitia] = new(10, 30, 20),
            [TroopEnums.AshWarden] = new(30, 55, 40),
            [TroopEnums.KhopeshWarrior] = new(65, 50, 20),
            [TroopEnums.SopduExplorer] = new(0, 20, 10),
            [TroopEnums.AnhurGuard] = new(50, 110, 50),
            [TroopEnums.ReshephChariot] = new(110, 120, 150),
            [TroopEnums.EgyptianRam] = new(60, 30, 80),
            [TroopEnums.EgyptianCatapult] = new(65, 55, 10),
            [TroopEnums.EgyptianChief] = new(40, 50, 40),
            [TroopEnums.EgyptianSettler] = new(0, 80, 80),

            // Huns (back-calculated - see class comment; Marksman/Marauder lowest confidence)
            [TroopEnums.Mercenary] = new(35, 40, 30),
            [TroopEnums.Bowman] = new(50, 30, 10),
            [TroopEnums.Spotter] = new(0, 10, 5),
            [TroopEnums.SteppeRider] = new(120, 30, 15),
            [TroopEnums.Marksman] = new(172, 120, 105),
            [TroopEnums.Marauder] = new(180, 60, 40),
            [TroopEnums.HunRam] = new(65, 30, 80),
            [TroopEnums.HunCatapult] = new(50, 60, 10),
            [TroopEnums.HunChief] = new(40, 60, 40),
            [TroopEnums.HunSettler] = new(0, 80, 80),
        };

        public static Stats Get(TroopEnums troop) => Table.TryGetValue(troop, out var stats) ? stats : Unknown;

        // Which of the defender's two totals (DefenseInfantry vs DefenseCavalry) an attacking
        // unit of this type is measured against - true = cavalry. Rams, catapults, chiefs and
        // settlers are infantry-type attackers for this purpose (real Travian mechanic), same as
        // every basic foot unit.
        public static bool IsCavalry(TroopEnums troop) => troop switch
        {
            TroopEnums.EquitesLegati or TroopEnums.EquitesImperatoris or TroopEnums.EquitesCaesaris => true,
            TroopEnums.Paladin or TroopEnums.TeutonicKnight => true,
            TroopEnums.Pathfinder or TroopEnums.TheutatesThunder or TroopEnums.Druidrider or TroopEnums.Haeduan => true,
            TroopEnums.SopduExplorer or TroopEnums.AnhurGuard or TroopEnums.ReshephChariot => true,
            TroopEnums.Spotter or TroopEnums.SteppeRider or TroopEnums.Marksman or TroopEnums.Marauder => true,
            _ => false,
        };
    }
}
