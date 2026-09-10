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

        private readonly UpdateAccountInfoCommand.Handler _updateAccountInfoCommand;
        private readonly UpdateVillageListCommand.Handler _updateVillageListCommand;
        private readonly UpdateAdventureCommand.Handler _updateAdventureCommand;
        private readonly DismissCookieConsentCommand.Handler _dismissCookieConsentCommand;

        public AccountTaskBehavior(IChromeBrowser browser, ITaskManager taskManager, UpdateAccountInfoCommand.Handler updateAccountInfoCommand, UpdateVillageListCommand.Handler updateVillageListCommand, UpdateAdventureCommand.Handler updateAdventureCommand, DismissCookieConsentCommand.Handler dismissCookieConsentCommand)
        {
            _browser = browser;
            _taskManager = taskManager;
            _updateAccountInfoCommand = updateAccountInfoCommand;
            _updateVillageListCommand = updateVillageListCommand;
            _updateAdventureCommand = updateAdventureCommand;
            _dismissCookieConsentCommand = dismissCookieConsentCommand;
        }

        public override async ValueTask<TResponse> HandleAsync(TRequest request, CancellationToken cancellationToken)
        {
            var accountId = request.AccountId;

            // 2026-08-26: a real "invalid session id" log showed the browser session dying
            // remotely (chromedriver/Chrome side) while _driver stays non-null locally, so
            // IsOpen (just a null check) doesn't catch it - the first _browser.Html read below
            // then throws a raw WebDriverException that nothing was catching, and the bot
            // paused the account instead of going through the BrowserClosed auto-recovery path
            // that already exists for the equivalent BiDi-side failure (see ChromeBrowser).
            // 2026-09-09: this protection had been lost from a later restructuring (the
            // unconditional DismissCookieConsentCommand call was added ahead of it without
            // being folded into the try) - restored, and widened to cover that call too, since
            // it also touches the driver (reads browser.Html - see DismissCookieConsentCommand
            // - and runs a JS script) and is now the very first thing every single task cycle
            // does, unconditionally.
            try
            {
                // Runs unconditionally, before the ingame/login-page branching below. The
                // consent overlay can appear on the LOGIN page too (before #servertime even
                // exists in the DOM), not just post-login — if this were gated behind
                // IsIngamePage, a modal sitting on top of the login form would never get
                // dismissed, and LoginCommand's subsequent browser.Click() on the login
                // button would silently land on the overlay instead (no exception, page never
                // navigates, WaitPageChanged("dorf") just times out).
                await _dismissCookieConsentCommand.HandleAsync(new(), cancellationToken);

                if (!LoginParser.IsIngamePage(_browser.Html))
                {
                    if (!LoginParser.IsLoginPage(_browser.Html))
                    {
                        return (TResponse)Stop.Error.WithError("Travian is not ingame nor login page. Please check browser");
                    }

                    if (request is not LoginTask.Task)
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
            }
            catch (WebDriverException)
            {
                await _browser.Close();
                return (TResponse)BrowserClosed.Error;
            }

            var response = await Next(request, cancellationToken);

            try
            {
                if (LoginParser.IsIngamePage(_browser.Html))
                {
                    await _updateAccountInfoCommand.HandleAsync(new(accountId), cancellationToken);
                    await _updateVillageListCommand.HandleAsync(new(accountId), cancellationToken);
                    await _updateAdventureCommand.HandleAsync(new(accountId), cancellationToken);
                }
            }
            catch (WebDriverException)
            {
                await _browser.Close();
                return (TResponse)BrowserClosed.Error;
            }

            return response;
        }
    }
}