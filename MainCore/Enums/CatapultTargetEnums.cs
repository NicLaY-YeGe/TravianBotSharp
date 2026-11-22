namespace MainCore.Enums
{
    // Values of the "troops[0][catapultTarget1]" <select> on the Rally Point confirmation screen,
    // taken from a real page capture (English client, 2026-11-08). 0 is OUR "not set" marker (the
    // form is left alone); Random (99) is the game's own default and is never selected explicitly.
    // Which entries the game offers depends on the sending village's Rally Point level (blog.travian.com,
    // "Game secrets - The use of catapults": 1 = random only, 3 = Warehouse/Granary, 5 = + resource
    // buildings, 10 = almost everything); the page itself only lists what is allowed, so the code
    // checks the page instead of the level. Catapults do not fire in a RAID, only in a normal attack.
    // Stored by numeric value (Raid List column) - never renumber, only append.
    public enum CatapultTargetEnums
    {
        NotSet = 0,
        Woodcutter = 1,
        ClayPit = 2,
        IronMine = 3,
        Cropland = 4,
        Sawmill = 5,
        Brickyard = 6,
        IronFoundry = 7,
        GrainMill = 8,
        Bakery = 9,
        Warehouse = 10,
        Granary = 11,
        Smithy = 13,
        TournamentSquare = 14,
        MainBuilding = 15,
        RallyPoint = 16,
        Marketplace = 17,
        Embassy = 18,
        Barracks = 19,
        Stable = 20,
        Workshop = 21,
        Academy = 22,
        TownHall = 24,
        Residence = 25,
        Palace = 26,
        Treasury = 27,
        TradeOffice = 28,
        Brewery = 35,
        HeroMansion = 37,
        Hospital = 46,
        Random = 99,
    }
}
