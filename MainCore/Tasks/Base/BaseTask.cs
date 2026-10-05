namespace MainCore.Tasks.Base
{
    public abstract class BaseTask : ITask
    {
        public virtual string Key { get; } = "";
        public StageEnums Stage { get; set; } = StageEnums.Waiting;
        public DateTime ExecuteAt { get; set; } = DateTime.Now;
        public virtual string Description { get; } = "";
        protected virtual string TaskName { get; } = "";

        public virtual bool CanStart(AppDbContext context) => true;

        // 2026-10-03, wake window for time-critical sends (see WakeWindowRules): a task that
        // returns true here is allowed to run in an "offline hour" (TimerManager normally skips
        // every task then), and WakeStart/WakeEnd describe the window around it in which the
        // account must be awake. All three are inert (false/null) for every ordinary task.
        public virtual bool BypassOnlineHours => false;
        public virtual DateTime? WakeStart => null;
        public virtual DateTime? WakeEnd => null;
    }
}