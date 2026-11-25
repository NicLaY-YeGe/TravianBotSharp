namespace MainCore.Commands.Features.UpgradeBuilding
{
    // 2026-11-19 (user report): the default build template (and any imported list) names resource fields by LOCATION and
    // TYPE ("clay at 5", "wood at 1"). The field types per location differ between villages (4-4-4-6, 3-5-4-6, 4-4-3-7 ...),
    // so on another layout the plan's slot holds another resource; FixJobsCommand dropped such jobs silently and the list
    // that was left differed from village to village.
    //
    // Now a plan's field is mapped by TYPE and ORDINAL: per type the distinct locations the list uses (ascending) are paired
    // with the village's fields of that type (ascending). A type whose locations all fit the village is left untouched (same
    // result as before for a village with the layout the list was made for). A list location without a counterpart (the
    // village has fewer fields of that type than the list uses) maps to -1 and its jobs are left out - the caller logs that.
    //
    // Pure and int-based (type codes are the BuildingEnums values 1..4) so it is unit tested without the enum.
    public static class ResourceFieldMapRules
    {
        public const int NoField = -1;

        private static bool IsField(int type) => type >= 1 && type <= 4;

        // listUses: (type, location) of every resource-field plan in the list.
        // villageFields: (location, type) of the village's fields (any building rows are fine, non-fields are ignored).
        // Result: (type, listLocation) -> location to use in this village, or NoField.
        public static Dictionary<(int Type, int Location), int> Build(
            IEnumerable<(int Type, int Location)> listUses,
            IEnumerable<(int Location, int Type)> villageFields)
        {
            var result = new Dictionary<(int Type, int Location), int>();
            var fields = villageFields.Where(x => IsField(x.Type)).ToList();

            foreach (var group in listUses.Where(x => IsField(x.Type)).Distinct().GroupBy(x => x.Type))
            {
                var type = group.Key;
                var listLocations = group.Select(x => x.Location).Distinct().OrderBy(x => x).ToList();
                var villageLocations = fields.Where(x => x.Type == type).Select(x => x.Location).Distinct().OrderBy(x => x).ToList();

                var allFit = listLocations.All(villageLocations.Contains);
                for (var i = 0; i < listLocations.Count; i++)
                {
                    int target;
                    if (allFit) target = listLocations[i];
                    else target = i < villageLocations.Count ? villageLocations[i] : NoField;
                    result[(type, listLocations[i])] = target;
                }
            }

            return result;
        }

        // Location a plan of (type, location) uses in this village; NoField when the list location has no counterpart.
        public static int Resolve(Dictionary<(int Type, int Location), int> map, int type, int location)
        {
            if (!IsField(type)) return location;
            return map.TryGetValue((type, location), out var target) ? target : NoField;
        }
    }
}
