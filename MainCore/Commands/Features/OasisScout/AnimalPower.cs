namespace MainCore.Commands.Features.OasisScout
{
    // Rough (not simulator-accurate) attack-power table for wild/nature oasis animals, used
    // only to decide whether it's "safe enough" to send the hero alone into an oasis - see
    // OasisScoutTask. Sourced from Travian's published T4/Legends standard-speed troop stats
    // (2026-09-15, cross-checked against two independent sources). This is NOT a real combat
    // simulation - no defense, no hero equipment bonus, no critical hits, just a sum of raw
    // attack points compared against the hero's own "Fighting strength" attack stat
    // (HeroParser.GetAttackPower). User-approved as a deliberately rough estimate ("kaba
    // tahmin"), tunable later via OasisScoutHeroPowerThreshold if it proves too
    // cautious/reckless in practice.
    public static class AnimalPower
    {
        private static readonly IReadOnlyDictionary<TroopEnums, int> Attack = new Dictionary<TroopEnums, int>
        {
            [TroopEnums.Rat] = 10,
            [TroopEnums.Spider] = 20,
            [TroopEnums.Snake] = 60,
            [TroopEnums.Bat] = 80,
            [TroopEnums.WildBoar] = 50,
            [TroopEnums.Wolf] = 100,
            [TroopEnums.Bear] = 250,
            [TroopEnums.Tiger] = 200,
            [TroopEnums.Crocodile] = 450,
            [TroopEnums.Elephant] = 600,
        };

        // Recognizes both the singular and the plural display names Travian's own UI shows
        // when an oasis has more than one of a kind (e.g. "Wolves", "Bears") - confirmed
        // against a real oasis tile-detail capture (2026-09-15). Case-insensitive.
        private static readonly IReadOnlyDictionary<string, TroopEnums> NameLookup = new Dictionary<string, TroopEnums>(StringComparer.OrdinalIgnoreCase)
        {
            ["Rat"] = TroopEnums.Rat,
            ["Rats"] = TroopEnums.Rat,
            ["Spider"] = TroopEnums.Spider,
            ["Spiders"] = TroopEnums.Spider,
            ["Snake"] = TroopEnums.Snake,
            ["Snakes"] = TroopEnums.Snake,
            ["Bat"] = TroopEnums.Bat,
            ["Bats"] = TroopEnums.Bat,
            ["Wild Boar"] = TroopEnums.WildBoar,
            ["Wild Boars"] = TroopEnums.WildBoar,
            ["Wolf"] = TroopEnums.Wolf,
            ["Wolves"] = TroopEnums.Wolf,
            ["Bear"] = TroopEnums.Bear,
            ["Bears"] = TroopEnums.Bear,
            ["Tiger"] = TroopEnums.Tiger,
            ["Tigers"] = TroopEnums.Tiger,
            ["Crocodile"] = TroopEnums.Crocodile,
            ["Crocodiles"] = TroopEnums.Crocodile,
            ["Elephant"] = TroopEnums.Elephant,
            ["Elephants"] = TroopEnums.Elephant,
        };

        // An unrecognized name (a future new animal type, a different game-client language, or
        // a parsing miss) is treated as "unknown risk" rather than silently zero power - falls
        // back to a conservative estimate sitting between Bear/Tiger and Crocodile.
        private const int UnknownAnimalPower = 300;

        public static int TotalPower(IEnumerable<(string Name, int Count)> animals)
        {
            var total = 0;
            foreach (var (name, count) in animals)
            {
                var power = NameLookup.TryGetValue(name.Trim(), out var troop) && Attack.TryGetValue(troop, out var att)
                    ? att
                    : UnknownAnimalPower;
                total += power * count;
            }
            return total;
        }
    }
}
