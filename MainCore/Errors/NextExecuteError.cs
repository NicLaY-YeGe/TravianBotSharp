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

        // 2026-11-22: the first job waits for resources and the task knows when to look again - a planned wait,
        // not a failed task (31 "failed" + 62 shortage warnings in 6 h, 5 of 8 follow the SUCCESSFUL upgrade of the same run).
        public static NextExecuteError ResourcesMissing(DateTime nextExecute, string detail)
           => new($"Waiting for resources: {detail}") { NextExecute = nextExecute };

        public static NextExecuteError PrerequisiteBuildingInQueue(BuildingEnums prerequisiteBuilding, int level, DateTime completeTime)
           => new($"{prerequisiteBuilding} level {level} is in queue")
           {
               NextExecute = completeTime,
               PrerequisiteType = prerequisiteBuilding,
               PrerequisiteLevel = level,
           };
    }
}