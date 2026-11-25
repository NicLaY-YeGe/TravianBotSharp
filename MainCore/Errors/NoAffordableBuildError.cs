namespace MainCore.Errors
{
    // 2026-11-23: the flexible build order found no job behind the first one that can be paid for right now. A normal outcome
    // (the task then waits for the first job), logged as Information instead of a Warning (7 warnings in 10 h).
    public class NoAffordableBuildError : Error
    {
        public NoAffordableBuildError() : base("No job behind the first one can be paid for right now.")
        {
        }
    }
}
