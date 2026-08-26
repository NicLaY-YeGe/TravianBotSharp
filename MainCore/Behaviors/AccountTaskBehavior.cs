using MainCore.Tasks.Base;

namespace MainCore.Behaviors
{
    public sealed class AccountTaskBehavior<TRequest, TResponse>
            : Behavior<TRequest, TResponse>
                where TRequest : AccountTask
                where TResponse : Result
    {
        private readonly ITaskManager _taskManager;
        private readonly IChromeBrowser _browser;
        private readonly ILogger _logger;

        private readonly UpdateAccountInfoCommand.Handler _updateAccountInfoCommand;
        private readonly UpdateVillageListCommand.Handler _updateVillageListCommand;
        private readonly UpdateAdventureCommand.Handler _updateAdventureCommand;

        // 2026-08-26: how many times to refresh-and-recheck before giving up. A page that's
        // neither ingame nor login most often means a transient network hiccup (e.g. Chrome's
        // own "This site can't be reached" / ERR_CONNECTION_TIMED_OUT) rather than something
        // wrong with the bot or the account - see the retry loop below for why this no longer
        // stops (and pauses the account, requiring the user to notice and resume) on the very
        // first occurrence.
        private const int MaxReconnectAttempts = 5;

        private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(30);

        public AccountTaskBehavior(IChromeBrowser browser, ITaskManager taskManager, ILogger logger, UpdateAccountInfoCommand.Handler updateAccountInfoCommand, UpdateVillageListCommand.Handler updateVillageListCommand, UpdateAdventureCommand.Handler updateAdventureCommand)
        {
            _browser = browser;
            _taskManager = taskManager;
            _logger = logger;
            _updateAccountInfoCommand = updateAccountInfoCommand;
            _updateVillageListCommand = updateVillageListCommand;
            _updateAdventureCommand = updateAdventureCommand;
        }

        public override async ValueTask<TResponse> HandleAsync(TRequest request, CancellationToken cancellationToken)
        {
            var accountId = request.AccountId;
            if (!LoginParser.IsIngamePage(_browser.Html))
            {
                if (!LoginParser.IsLoginPage(_browser.Html))
                {
                    // 2026-08-26: neither ingame nor login page - try to recover from what's
                    // usually a transient connection problem before treating it as fatal.
                    // Refresh a few times with a short delay between attempts; Html is a live
                    // property (re-reads _driver.PageSource each access), so re-checking after
                    // each refresh picks up the current state automatically.
                    for (var attempt = 1; attempt <= MaxReconnectAttempts; attempt++)
                    {
                        _logger.Warning("Page is neither ingame nor login page (attempt {Attempt}/{MaxAttempts}). Waiting {DelaySeconds}s then refreshing.", attempt, MaxReconnectAttempts, ReconnectDelay.TotalSeconds);
                        await Task.Delay(ReconnectDelay, cancellationToken);

                        var refreshResult = await _browser.Refresh(cancellationToken);
                        if (refreshResult.IsFailed)
                        {
                            // Browser itself is gone (e.g. BrowserClosed) - let the existing
                            // BrowserClosed handling in TimerManager deal with it (relaunches
                            // Chrome automatically on the next tick) instead of masking it here.
                            return (TResponse)refreshResult;
                        }

                        if (LoginParser.IsIngamePage(_browser.Html) || LoginParser.IsLoginPage(_browser.Html))
                        {
                            _logger.Information("Connection recovered after {Attempt} refresh attempt(s).", attempt);
                            break;
                        }
                    }

                    if (!LoginParser.IsIngamePage(_browser.Html) && !LoginParser.IsLoginPage(_browser.Html))
                    {
                        return (TResponse)Stop.Error.WithError($"Travian is not ingame nor login page after {MaxReconnectAttempts} refresh attempts, {ReconnectDelay.TotalSeconds}s apart. Please check browser");
                    }
                }

                // 2026-08-26: a refresh above may have recovered straight into the ingame
                // page (transient blip, session was still valid) rather than the login page -
                // only treat this as "needs re-login" if it's genuinely still not ingame.
                if (!LoginParser.IsIngamePage(_browser.Html) && request is not LoginTask.Task)
                {
                    _taskManager.AddOrUpdate<LoginTask.Task>(new(accountId), first: true);
                    request.ExecuteAt = request.ExecuteAt.AddSeconds(1);
                    return (TResponse)Skip.Error.WithError("Account is logout. Re-login now");
                }
            }

            if (LoginParser.IsIngamePage(_browser.Html))
            {
                await _updateAccountInfoCommand.HandleAsync(new(accountId), cancellationToken);
                await _updateVillageListCommand.HandleAsync(new(accountId), cancellationToken);
            }

            var response = await Next(request, cancellationToken);

            if (LoginParser.IsIngamePage(_browser.Html))
            {
                await _updateAccountInfoCommand.HandleAsync(new(accountId), cancellationToken);
                await _updateVillageListCommand.HandleAsync(new(accountId), cancellationToken);
                await _updateAdventureCommand.HandleAsync(new(accountId), cancellationToken);
            }

            return response;
        }
    }
}