namespace MainCore.Errors
{
    public class NextExecuteError : Error
    {
        public DateTime NextExecute { get; set; }

        // Only set for PrerequisiteBuildingInQueue - PrerequisiteLevel stays 0 (its default)
        // for ConstructionQueueFull, so it never accidentally matches a real prerequisite
        // (levels are always >= 1).
        public BuildingEnums PrerequisiteType { get; private init; }
        public int PrerequisiteLevel { get; private init; }

        private NextExecuteError(string message) : base(message)
        {
        }

        public static NextExecuteError ConstructionQueueFull(DateTime nextExecute)
           => new("Construction queue is full") { NextExecute = nextExecute };

        public static NextExecuteError PrerequisiteBuildingInQueue(BuildingEnums prerequisiteBuilding, int level, DateTime completeTime)
           => new($"{prerequisiteBuilding} level {level} is in queue")
           {
               NextExecute = completeTime,
               PrerequisiteType = prerequisiteBuilding,
               PrerequisiteLevel = level,
           };
    }
}