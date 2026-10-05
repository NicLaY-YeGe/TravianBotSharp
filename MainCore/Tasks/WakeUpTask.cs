using MainCore.Tasks.Base;

namespace MainCore.Tasks
{
    // 2026-10-03, user request ("çıkış saatinden önce uyan, gönder, uyu"): scheduled by
    // SyncAttackPlanTask at the START of a timed send's wake window. It is a task that is
    // allowed to run at that moment (BypassOnlineHours, so an offline hour doesn't hold it;
    // SleepCommand has already cut a running sleep short for the same window), which makes
    // TimerManager reopen the browser if it was closed. The handler then reloads the page and
    // queues the login right away if the session has expired - minutes before the send,
    // instead of eating into the send's own time budget.
    //
    // Not persisted and not re-created on restart, like the SendTroopsAtTimeTask it belongs
    // to (the whole queue is rebuilt by RxQueue.AccountInitHandler after a Restart, and a
    // timed send never survived that either) - deliberately NOT in AccountInitHandler.
    [Handler]
    public static partial class WakeUpTask
    {
        public sealed class Task : AccountTask
        {
            public Task(AccountId accountId, DateTime wakeFrom) : base(accountId)
            {
                ExecuteAt = wakeFrom;
            }

            public override bool BypassOnlineHours => true;

            protected override string TaskName => "Wake up for timed send";
        }

        private static async ValueTask<Result> HandleAsync(
            Task task,
            IChromeBrowser browser,
            ITaskManager taskManager,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            // A browser that sat idle through the offline hours still shows the last page it
            // had - possibly an in-game page whose session has long expired. AccountTaskBehavior
            // only looks at that stale page, so without a reload the expired session would
            // first be noticed by the SEND itself. Reloading shows the truth; if it is the
            // login page, queue the login now (it is allowed through while the window is open).
            var refreshResult = await browser.Refresh(cancellationToken);
            if (refreshResult.IsFailed) return refreshResult;

            if (LoginParser.IsLoginPage(browser.Html))
            {
                taskManager.AddOrUpdate<LoginTask.Task>(new(task.AccountId), first: true);
                logger.Information("Awake for a timed send - session expired, logging in ahead of time.");
            }
            else
            {
                logger.Information("Awake and logged in for a timed send.");
            }

            return Result.Ok();
        }
    }
}
