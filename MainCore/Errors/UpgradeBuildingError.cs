namespace MainCore.Errors
{
    public class UpgradeBuildingError : Error
    {
        private UpgradeBuildingError(string message) : base(message)
        {
        }

        // Only set for PrerequisiteBuildingMissing - PrerequisiteLevel stays 0 (its default)
        // for every other case, which is what callers use to tell them apart without a type
        // check (a real prerequisite level is always >= 1).
        public BuildingEnums PrerequisiteType { get; private init; }
        public int PrerequisiteLevel { get; private init; }

        // Only set for SlotOccupied: the plan's slot holds a different building (see BuildPlanSlotRules).
        public bool IsSlotOccupied { get; private init; }

        // Only set for UpgradeButtonMissing: the build/upgrade page had no button to click (2026-11-17).
        public bool IsButtonMissing { get; private init; }

        public static UpgradeBuildingError UpgradeButtonMissing(BuildingEnums building, int location, string pageHint)
        {
            var message = $"no upgrade button for {building} at location {location} on the page";
            if (!string.IsNullOrWhiteSpace(pageHint)) message += $" (page says: {pageHint})";
            return new UpgradeBuildingError(message) { IsButtonMissing = true };
        }

        public static UpgradeBuildingError BuildingJobQueueEmpty
            => new("Building job queue is empty");

        public static UpgradeBuildingError BuildingJobQueueBroken
            => new("Building job queue is broken. No building in construct but cannot choose job");

        public static UpgradeBuildingError SlotOccupied(BuildingEnums planned, int location, BuildingEnums actual, int actualLevel)
            => new($"slot {location} holds {actual} level {actualLevel}, not {planned}")
            {
                IsSlotOccupied = true,
            };

        public static UpgradeBuildingError PrerequisiteBuildingMissing(BuildingEnums prerequisiteBuilding, int level)
            => new($"{prerequisiteBuilding} level {level} is missing")
            {
                PrerequisiteType = prerequisiteBuilding,
                PrerequisiteLevel = level,
            };
    }
}