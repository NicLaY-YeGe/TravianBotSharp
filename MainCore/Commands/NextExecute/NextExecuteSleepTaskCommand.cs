using MainCore.Commands.Features.SyncAttack;

namespace MainCore.Commands.NextExecute
{
    [Handler]
    public static partial class NextExecuteSleepTaskCommand
    {
        public sealed record Command(SleepTask.Task Task) : ICommand;

        private static async ValueTask HandleAsync(
            Command command,
            ISettingService settingService,
            ITaskManager taskManager
            )
        {
            await Task.CompletedTask;
            var workTime = settingService.ByName(
                command.Task.AccountId,
                AccountSettingEnums.WorkTimeMin,
                AccountSettingEnums.WorkTimeMax,
                60);

            // 2026-10-03, wake window: if this sleep was cut short for a timed send (its window
            // has opened and the send is still pending), don't start a whole work period -
            // sleep again once the window is over ("gönder, uyu"). Plain work period otherwise.
            var now = DateTime.Now;
            var starts = taskManager.GetTaskList(command.Task.AccountId).ToArray().Select(t => (t.WakeStart, t.WakeEnd));
            var sleepAgainAt = WakeWindowRules.NextSleepAfterWake(starts, now);
            command.Task.ExecuteAt = sleepAgainAt ?? now.AddSeconds(workTime);
        }
    }
}