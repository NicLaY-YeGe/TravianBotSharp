namespace MainCore.Commands.UI.MainLayoutViewModel
{
    [Handler]
    public static partial class LoginCommand
    {
        public sealed record Command(AccountId AccountId, AccessDto Access) : IAccountCommand;

        private static async ValueTask HandleAsync(
            Command command,
            ITaskManager taskManager,
            ITimerManager timerManager,
            ILogger logger,
            IRxQueue rxQueue,
            OpenBrowserCommand.Handler openBrowserCommand,
            CancellationToken cancellationToken
            )
        {
            var (accountId, access) = command;

            logger.Information("Using connection {Proxy} to start chrome", access.Proxy);

            try
            {
                taskManager.SetStatus(accountId, StatusEnums.Starting);
                await openBrowserCommand.HandleAsync(new(accountId, access), cancellationToken);
            }
            catch (Exception ex)
            {
                // 2026-10-21: this used to swallow the exception completely, so a browser that
                // failed to start (first Playwright run: nothing in the log, account just went
                // Offline) left no trace at all. Log the full exception.
                logger.Error(ex, "Could not start the browser: {Type}: {Message}", ex.GetType().Name, ex.Message);
                taskManager.SetStatus(accountId, StatusEnums.Offline);
                return;
            }

            timerManager.Start(accountId);
            taskManager.SetStatus(accountId, StatusEnums.Online);
            rxQueue.Enqueue(new AccountInit(accountId));
        }
    }
}