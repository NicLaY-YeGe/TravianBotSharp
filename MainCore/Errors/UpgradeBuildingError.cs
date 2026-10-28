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