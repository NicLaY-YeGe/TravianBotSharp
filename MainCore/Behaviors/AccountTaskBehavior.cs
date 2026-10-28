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

        // Delay before a task whose page was still navigating runs again (short: Dodge/Recall are time-critical).
        private const int TransientRetrySeconds = 10;

        private readonly UpdateAccountInfoCommand.Handler _updateAccountInfoCommand;
        private readonly UpdateVillageListCommand.Handler _updateVillageListCommand;
        private readonly UpdateAdventureCommand.Handler _updateAdventureCommand;
        private readonly DismissCookieConsentCommand.Handler _dismissCookieConsentCommand;

        public AccountTaskBehavior(IChromeBrowser browser, ILogger logger, ITaskManager taskManager, UpdateAccountInfoCommand.Handler updateAccountInfoCommand, UpdateVillageListCommand.Handler updateVillageListCommand, UpdateAdventureCommand.Handler updateAdventureCommand, DismissCookieConsentCommand.Handler dismissCookieConsentCommand)
        {
            _browser = browser;
            _logger = logger;
            _taskManager = taskManager;
            _updateAccountInfoCommand = updateAccountInfoCommand;
            _updateVillageListCommand = updateVillageListCommand;
            _updateAdventureCommand = updateAdventureCommand;
            _dismissCookieConsentCommand = dismissCookieConsentCommand;
        }

        public override async ValueTask<TResponse> HandleAsync(TRequest request, CancellationToken cancellationToken)
        {
            var accountId = request.AccountId;

            // 2026-10-18: moved to Playwright - the raw WebDriverException below is now
            // BrowserException (thrown by the browser.Html / element reads when the page or
            // browser is gone); the recovery path is unchanged.
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
                    // 2026-10-18 (real TS32 incident: 268 "not ingame nor login" skips over 8
                    // hours while the browser sat on a login screen): a page that is neither
                    // in-game nor login is often just half-loaded or stuck (the login form is
                    // rendered client-side by React), not a down site. Try ONE reload right
                    // now before settling for the 30-minute backoff below, and re-judge the
                    // page afterwards - the reload may well land on the game or the login form.
                    if (!LoginParser.IsLoginPage(_browser.Html))
                    {
                        var refreshResult = await _browser.Refresh(cancellationToken);
                        if (refreshResult.IsSuccess)
                        {
                            await _dismissCookieConsentCommand.HandleAsync(new(), cancellationToken);
                        }
                    }

                    if (!LoginParser.IsIngamePage(_browser.Html))
                    {
                        if (!LoginParser.IsLoginPage(_browser.Html))
                        {
                            // The page is neither in-game nor the login page - most often this
                            // means the site itself was temporarily unreachable (Chrome's own
                            // "ERR_CONNECTION_TIMED_OUT" error page - confirmed via a user
                            // screenshot on 2026-09-11) rather than something that needs a human
                            // to step in. This used to pause the WHOLE account until the user
                            // noticed and manually resumed it. Now it reschedules this same task
                            // 30 minutes out instead (same backoff pattern as the MissingResource
                            // case in UpgradeBuildingTask) and keeps going - no retry cap, matching
                            // the "just keep retrying automatically" philosophy already used for
                            // the browser-reopen path above. Trade-off, on purpose: this also
                            // silently retries the rarer non-transient cases (account banned,
                            // page structure actually changed) every 30 minutes forever instead of
                            // surfacing them as a paused account with a notification - if that
                            // turns out to be a problem in practice, a retry-count cap before
                            // finally pausing would be the next step.
                            //
                            // 2026-10-18: the message now carries what the page actually was,
                            // so the next incident can be diagnosed from the log alone.
                            var title = _browser.Html.DocumentNode.SelectSingleNode("//title")?.InnerText?.Trim();
                            var bodyClass = _browser.Html.DocumentNode.SelectSingleNode("//body")?.GetAttributeValue("class", "");
                            request.ExecuteAt = request.ExecuteAt.AddMinutes(30);
                            return (TResponse)Skip.Error.WithError(
                                $"Travian is not ingame nor login page (site may be unreachable) - retrying in 30 minutes [url={_browser.CurrentUrl}, title='{title}', body class='{bodyClass}']");
                        }

                        if (request is not LoginTask.Task)
                        {
                            _taskManager.AddOrUpdate<LoginTask.Task>(new(accountId), first: true);
                            request.ExecuteAt = request.ExecuteAt.AddSeconds(1);
                            return (TResponse)Skip.Error.WithError("Account is logout. Re-login now");
                        }
                    }
                }

                if (LoginParser.IsIngamePage(_browser.Html))
                {
                    await _updateAccountInfoCommand.HandleAsync(new(accountId), cancellationToken);
                    await _updateVillageListCommand.HandleAsync(new(accountId), cancellationToken);
                }
            }
            catch (BrowserException ex) when (BrowserErrorRules.IsTransient(ex.Message))
            {
                // 2026-10-28: the page was still navigating after the reads were repeated (SyncBridge.RunRetryingTransient).
                // The browser is healthy - do NOT close it (that cost a re-open and a full re-login, 5x in 96 min).
                // Come back shortly; ExecuteAt must change or TimerManager removes the task (CLAUDE.md rule 16).
                request.ExecuteAt = DateTime.Now.AddSeconds(TransientRetrySeconds);
                _logger.Information("{Task}: the page was still navigating, trying again in {Seconds}s ({Message})", request.Description, TransientRetrySeconds, ex.Message);
                return (TResponse)Skip.Error.WithError("The page was still navigating - trying again shortly.");
            }
            catch (BrowserException ex)
            {
                // Log WHY the browser is treated as gone; the old code swallowed the message.
                _logger.Warning("Browser treated as closed: {Message}", ex.Message);
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
            catch (BrowserException ex) when (BrowserErrorRules.IsTransient(ex.Message))
            {
                // The task itself already finished; only the after-task refresh hit a navigation. Keep the task's
                // own result - the next cycle refreshes the account data again.
                _logger.Information("{Task}: skipped the after-task refresh, the page was still navigating ({Message})", request.Description, ex.Message);
                return response;
            }
            catch (BrowserException ex)
            {
                _logger.Warning("Browser treated as closed: {Message}", ex.Message);
                await _browser.Close();
                return (TResponse)BrowserClosed.Error;
            }

            return response;
        }
    }
}