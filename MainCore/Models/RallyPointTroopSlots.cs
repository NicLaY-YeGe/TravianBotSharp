namespace MainCore.Models
{
    // Slot order (1-10) as shown in the Rally Point "Send troops" form: infantry, cavalry,
    // siege, chief/administrator, settler. This is the same "troopSlot 1-10, tribe-relative"
    // convention already used by SendReinforcementCommand/RallyPointSendTroopsParser, and it
    // lines up with the declaration order of each tribe's block in TroopEnums.
    //
    // NOT yet verified against a real page capture for every tribe (only the general form
    // mechanics were verified for DodgeTroop). If a live account ever shows a mismatch between
    // a slot here and the actual column in-game, fix this table first before suspecting the
    // parser - see CLAUDE.md §2d for the project's general policy on trusting real captures
    // over assumptions.
    public static class RallyPointTroopSlots
    {
        public static IReadOnlyList<TroopEnums> GetSlots(TribeEnums tribe)
        {
            return tribe switch
            {
                TribeEnums.Romans =>
                [
                    TroopEnums.Legionnaire, TroopEnums.Praetorian, TroopEnums.Imperian,
                    TroopEnums.EquitesLegati, TroopEnums.EquitesImperatoris, TroopEnums.EquitesCaesaris,
                    TroopEnums.RomanRam, TroopEnums.RomanCatapult, TroopEnums.RomanChief, TroopEnums.RomanSettler,
                ],
                TribeEnums.Teutons =>
                [
                    TroopEnums.Clubswinger, TroopEnums.Spearman, TroopEnums.Axeman, TroopEnums.Scout,
                    TroopEnums.Paladin, TroopEnums.TeutonicKnight,
                    TroopEnums.TeutonRam, TroopEnums.TeutonCatapult, TroopEnums.TeutonChief, TroopEnums.TeutonSettler,
                ],
                TribeEnums.Gauls =>
                [
                    TroopEnums.Phalanx, TroopEnums.Swordsman,
                    TroopEnums.Pathfinder, TroopEnums.TheutatesThunder, TroopEnums.Druidrider, TroopEnums.Haeduan,
                    TroopEnums.GaulRam, TroopEnums.GaulCatapult, TroopEnums.GaulChief, TroopEnums.GaulSettler,
                ],
                TribeEnums.Egyptians =>
                [
                    TroopEnums.SlaveMilitia, TroopEnums.AshWarden, TroopEnums.KhopeshWarrior,
                    TroopEnums.SopduExplorer, TroopEnums.AnhurGuard, TroopEnums.ReshephChariot,
                    TroopEnums.EgyptianRam, TroopEnums.EgyptianCatapult, TroopEnums.EgyptianChief, TroopEnums.EgyptianSettler,
                ],
                TribeEnums.Huns =>
                [
                    TroopEnums.Mercenary, TroopEnums.Bowman,
                    TroopEnums.Spotter, TroopEnums.SteppeRider, TroopEnums.Marksman, TroopEnums.Marauder,
                    TroopEnums.HunRam, TroopEnums.HunCatapult, TroopEnums.HunChief, TroopEnums.HunSettler,
                ],
                _ => [],
            };
        }
    }
}
