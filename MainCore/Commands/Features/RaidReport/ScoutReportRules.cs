using MainCore.Parsers;

namespace MainCore.Commands.Features.RaidReport
{
    // Pure decision logic for what a scouting report says about a raid target, kept free of
    // any DB/browser access like RaidReportRules.
    public static class ScoutReportRules
    {
        // What a raid can actually take from a village that a scouting report showed: a
        // cranny hides up to its capacity of EACH resource type, so the stealable amount of a
        // type is max(0, amount - cranny). The capacity is the value the report shows - it
        // does not model tribe bonuses (Gaul cranny bonus, Teuton cranny-ignoring raids), so
        // this is an estimate. A missing cranny line (null) counts as no cranny.
        public static int EstimatePlunderable(ScoutResources resources, int? crannyCapacity)
        {
            var hidden = Math.Max(0, crannyCapacity ?? 0);

            return Stealable(resources.Lumber, hidden)
                + Stealable(resources.Clay, hidden)
                + Stealable(resources.Iron, hidden)
                + Stealable(resources.Crop, hidden);
        }

        private static int Stealable(int amount, int hidden) => Math.Max(0, amount - hidden);
    }
}
