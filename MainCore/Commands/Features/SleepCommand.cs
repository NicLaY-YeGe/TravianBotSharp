using MainCore.Commands.Features.SyncAttack;

namespace MainCore.Commands.Features
{
    [Handler]
    public static partial class SleepCommand
    {
        public sealed record Command(AccountId AccountId) : IAccountCommand;

        private static async ValueTask<Result> HandleAsync(
            Command command,
            IChromeBrowser browser,
            ISettingService settingService,
            ITaskManager taskManager,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            await browser.Close();

            var sleepTimeMinutes = settingService.ByName(command.AccountId, AccountSettingEnums.SleepTimeMin, AccountSettingEnums.SleepTimeMax);
            var sleepEnd = DateTime.Now.AddMinutes(sleepTimeMinutes);
            int lastMinute = 0;
            while (true)
            {
                if (cancellationToken.IsCancellationRequested) return Cancel.Error;

                var timeRemaining = sleepEnd - DateTime.Now;
                if (timeRemaining < TimeSpan.Zero) return Result.Ok();

                // 2026-10-03, wake window: a timed send (Sync Attack "wake up for the send")
                // has reached the start of its window - cut the sleep short so the browser is
                // reopened and logged in BEFORE the send is due. SleepTask then reopens the
                // browser and NextExecuteSleepTaskCommand schedules the next sleep right
                // after the window instead of a full work period.
                if (HasOpenWakeWindow(taskManager, command.AccountId))
                {
                    logger.Information("A timed send is about to be due - waking up early.");
                    return Result.Ok();
                }

                await Task.Delay(TimeSpan.FromSeconds(1), CancellationToken.None);

                var currentMinute = (int)timeRemaining.TotalMinutes;
                if (lastMinute != currentMinute)
                {
                    logger.Information("Chrome will reopen in {CurrentMinute} mins", currentMinute);
                    lastMinute = currentMinute;
                }
            }
        }

        // Same snapshot-then-inspect approach as everywhere else the task list is read from
        // another thread; any race just means "ask again in a second".
        private static bool HasOpenWakeWindow(ITaskManager taskManager, AccountId accountId)
        {
            try
            {
                var starts = taskManager.GetTaskList(accountId).ToArray().Select(t => t.WakeStart);
                return WakeWindowRules.HasOpenWindow(starts, DateTime.Now);
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }
}