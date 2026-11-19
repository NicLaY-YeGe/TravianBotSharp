namespace MainCore.Commands.Features.UpgradeBuilding
{
    // 2026-11-04 (user request, after a village was razed by catapults): without a Main Building nothing else can be
    // constructed in the village, and without a Warehouse / Granary nothing can be stored. If an attack destroys one of
    // these while nobody is watching, the whole build list is stuck. This rule says which of the three the village is
    // MISSING and where to rebuild it; the jobs are queued at the top of the build list up to level 3.
    //
    // "Missing" = no entry of that type in the village layout. The layout passed in is the merged one of
    // GetLayoutBuildingsCommand (built + under construction + queued as a job), so a rebuild that is already queued is
    // never queued twice. Pure (no browser, no database), unit tested.
    public sealed record EssentialPlan(BuildingEnums Type, int Location, int Level);

    public static class EssentialBuildingRules
    {
        // Rebuilt to this level ("at least level 3", user decision).
        public const int TargetLevel = 3;

        // The Main Building always stands on slot 26, the Rally Point on 39, the Wall on 40.
        public const int MainBuildingLocation = 26;
        public const int RallyPointLocation = 39;
        public const int WallLocation = 40;

        // Infrastructure slots are 19..40. A layout that holds fewer entries than this has not been read from the village
        // page yet (new village, parse glitch): then nothing is "missing" - it is simply unknown.
        public const int FirstInfrastructureLocation = 19;
        public const int MinScannedInfrastructure = 18;

        public static bool IsLayoutScanned(IReadOnlyCollection<BuildingItem> buildings)
        {
            return buildings.Count(x => x.Location >= FirstInfrastructureLocation) >= MinScannedInfrastructure;
        }

        // Plans in the order they must be QUEUED when every plan is put on top of the list: Granary, Warehouse, then the
        // Main Building, so the Main Building ends up first.
        public static List<EssentialPlan> Plan(IReadOnlyCollection<BuildingItem> buildings)
        {
            var plans = new List<EssentialPlan>();
            if (!IsLayoutScanned(buildings)) return plans;

            var taken = new HashSet<int>();

            foreach (var type in new[] { BuildingEnums.Granary, BuildingEnums.Warehouse })
            {
                if (buildings.Any(x => x.Type == type)) continue;

                var site = buildings
                    .Where(x => x.Type == BuildingEnums.Site)
                    .Where(x => x.Location >= FirstInfrastructureLocation && x.Location < WallLocation)
                    .Where(x => x.Location != MainBuildingLocation && x.Location != RallyPointLocation)
                    .Where(x => !taken.Contains(x.Location))
                    .OrderBy(x => x.Location)
                    .FirstOrDefault();
                if (site is null) continue;

                taken.Add(site.Location);
                plans.Add(new EssentialPlan(type, site.Location, TargetLevel));
            }

            if (!buildings.Any(x => x.Type == BuildingEnums.MainBuilding))
            {
                var slot = buildings.FirstOrDefault(x => x.Location == MainBuildingLocation);
                if (slot is not null && slot.Type == BuildingEnums.Site)
                {
                    plans.Add(new EssentialPlan(BuildingEnums.MainBuilding, MainBuildingLocation, TargetLevel));
                }
            }

            return plans;
        }
    }
}
