using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.Retry;
using Timer = System.Timers.Timer;

namespace MainCore.Services
{
    [RegisterSingleton<ITimerManager, TimerManager>]
    public sealed class TimerManager : ITimerManager
    {
        private readonly Dictionary<AccountId, Timer> _timers = [];

        private bool _isShutdown = false;

        private readonly ITaskManager _taskManager;
        private readonly IRxQueue _rxQueue;
        private readonly ICustomServiceScopeFactory _serviceScopeFactory;
        private readonly ITelegramNotifier _telegramNotifier;

        private static ResiliencePropertyKey<ContextData> contextDataKey = new(nameof(ContextData));
        private readonly ResiliencePipeline<Result> _pipeline;

        public TimerManager(ITaskManager taskManager, ICustomServiceScopeFactory serviceScopeFactory, IRxQueue rxQueue, ITelegramNotifier telegramNotifier)
        {
            _taskManager = taskManager;
            _serviceScopeFactory = serviceScopeFactory;
            _rxQueue = rxQueue;
            _telegramNotifier = telegramNotifier;

            Func<OnRetryArguments<Result>, ValueTask> OnRetry = async static args =>
            {
                await Task.CompletedTask;
                if (!args.Context.Properties.TryGetValue(contextDataKey, out var contextData)) return;

                var (taskName, browser) = contextData;
                var error = args.Outcome;
                if (error.Exception is not null)
                {
                    var exception = error.Exception;
                    browser.Logger.Error(exception, "{Message}", exception.Message);
                }
                if (error.Result is not null)
                {
                    var message = string.Join(Environment.NewLine, error.Result.Reasons.Select(e => e.Message));
                    if (!string.IsNullOrEmpty(message))
                    {
                        browser.Logger.Warning("Task {TaskName} failed", taskName, message);
                        browser.Logger.Warning("{Message}", message);
                    }
                }

                browser.Logger.Warning("{TaskName} will retry after {RetryDelay} (#{AttemptNumber} times)", taskName, args.RetryDelay, args.AttemptNumber + 1);
            };

            var retryOptions = new RetryStrategyOptions<Result>()
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromSeconds(30),
                UseJitter = true,
                BackoffType = DelayBackoffType.Exponential,
                ShouldHandle = new PredicateBuilder<Result>()
                   .Handle<Exception>()
                   .HandleResult(static x => x.HasError<Retry>()),
                OnRetry = OnRetry
            };

            _pipeline = new ResiliencePipelineBuilder<Result>()
                .AddRetry(retryOptions)
                .Build();
        }

        public async Task Execute(AccountId accountId)
        {
            var taskQueue = _taskManager.GetTaskQueue(accountId);

            var status = taskQueue.Status;
            if (status != StatusEnums.Online) return;
            var tasks = taskQueue.Tasks;
            if (tasks.Count == 0) return;
            var task = tasks[0];

            if (task.ExecuteAt > DateTime.Now) return;

            using var scope = _serviceScopeFactory.CreateScope(accountId);

            // Account is configured to be "offline" during this hour of the day: leave the
            // browser open (if it already is) but don't start a new task. We just skip this
            // tick and try again on the next timer elapse.
            var settingService = scope.ServiceProvider.GetRequiredService<ISettingService>();
            if (!settingService.IsCurrentHourOnline(accountId)) return;

            taskQueue.IsExecuting = true;
            var cts = new CancellationTokenSource();
            taskQueue.CancellationTokenSource = cts;

            task.Stage = StageEnums.Executing;
            _rxQueue.Enqueue(new TasksModified(accountId));

            var cacheExecuteTime = task.ExecuteAt;

            ///===========================================================///
            var browser = scope.ServiceProvider.GetRequiredService<IChromeBrowser>();
            var logger = browser.Logger;

            if (!browser.IsOpen)
            {
                var opened = await EnsureBrowserOpen(accountId, scope, browser, cts.Token);
                if (!opened)
                {
                    // Chrome is closed (never opened yet, closed by hand, crashed) and we
                    // couldn't relaunch it this tick - most commonly because there's no
                    // working network/proxy access right now. Don't touch the queue or pause
                    // the account: back off and let the next tick try again, so a dropped
                    // connection recovers on its own once it's back instead of requiring a
                    // manual restart (2026-08-25).
                    task.Stage = StageEnums.Waiting;
                    _rxQueue.Enqueue(new TasksModified(accountId));

                    taskQueue.IsExecuting = false;
                    cts.Dispose();
                    taskQueue.CancellationTokenSource = null;

                    var retryDelayService = scope.ServiceProvider.GetRequiredService<IDelayService>();
                    await retryDelayService.DelayTask();
                    return;
                }
            }

            var contextData = new ContextData(task.Description, browser);

            ///===========================================================///
            var context = ResilienceContextPool.Shared.Get(cts.Token);

            context.Properties.Set(contextDataKey, contextData);

            var poliResult = await _pipeline.ExecuteOutcomeAsync(
                async (ctx, state) => Outcome.FromResult(await scope.Execute(state, ctx.CancellationToken)),
                context,
                task);

            ResilienceContextPool.Shared.Return(context);
            ///===========================================================///

            task.Stage = StageEnums.Waiting;
            _rxQueue.Enqueue(new TasksModified(accountId));

            taskQueue.IsExecuting = false;

            cts.Dispose();
            taskQueue.CancellationTokenSource = null;

            if (poliResult.Exception is not null)
            {
                var ex = poliResult.Exception;

                if (ex is OperationCanceledException)
                {
                    logger.Information("Pause button is pressed");
                }
                else
                {
                    var filename = await browser.Screenshot();
                    logger.Information("Screenshot saved as {FileName}", filename);
                    logger.Warning("There is something wrong. Bot is pausing. Last exception is");
                    logger.Error(ex, "{Message}", ex.Message);

                    await NotifyPaused(accountId, scope, $"Unexpected error: {ex.Message}");
                }

                _taskManager.SetStatus(accountId, StatusEnums.Paused);
            }

            if (poliResult.Result is not null)
            {
                var result = poliResult.Result;
                if (result.IsFailed)
                {
                    var message = string.Join(Environment.NewLine, result.Reasons.Select(e => e.Message));
                    if (!string.IsNullOrEmpty(message))
                    {
                        logger.Warning("Task {TaskName} failed", task.Description, message);
                        logger.Warning("{Message}", message);
                    }

                    if (result.HasError<BrowserClosed>())
                    {
                        // 2026-08-25: browser died mid-task (closed manually, crashed, or the
                        // connection dropped). Don't pause the account or remove/reorder the
                        // task - leave it at the head of the queue so the next tick's
                        // browser-open check (above) relaunches Chrome and retries it
                        // automatically instead of requiring the user to notice and restart.
                        logger.Warning("Browser was closed mid-task - will reopen automatically and retry.");
                    }
                    else if (result.HasError<Stop>() || result.HasError<Retry>())
                    {
                        var filename = await browser.Screenshot();
                        logger.Information(messageTemplate: "Screenshot saved as {FileName}", filename);
                        await NotifyPaused(accountId, scope, message);
                        _taskManager.SetStatus(accountId, StatusEnums.Paused);
                    }
                    else if (result.HasError<Skip>())
                    {
                        if (task.ExecuteAt == cacheExecuteTime)
                        {
                            _taskManager.Remove(accountId, task);
                        }
                        else
                        {
                            _taskManager.ReOrder(accountId);
                            logger.Information("Schedule next run at {Time}", task.ExecuteAt.ToString("yyyy-MM-dd HH:mm:ss"));
                        }
                    }
                    else if (result.HasError<Cancel>())
                    {
                        await NotifyPaused(accountId, scope, message);
                        _taskManager.SetStatus(accountId, StatusEnums.Paused);
                    }
                }
                else
                {
                    if (task.ExecuteAt == cacheExecuteTime)
                    {
                        _taskManager.Remove(accountId, task);
                    }
                    else
                    {
                        _taskManager.ReOrder(accountId);
                        logger.Information("Schedule next run at {Time}", task.ExecuteAt.ToString("yyyy-MM-dd HH:mm:ss"));
                    }
                }
            }

            var delayService = scope.ServiceProvider.GetRequiredService<IDelayService>();
            await delayService.DelayTask();
        }

        // Chrome can end up closed for reasons outside the bot's control - the user closes it
        // by accident, it crashes, or this is simply the first task ever run for the account.
        // Called before running the next queued task (see Execute) so it gets relaunched
        // automatically instead of failing against a dead/missing driver. GetValidAccessCommand
        // already checks connectivity through the account's proxy as part of picking an access,
        // so a dropped network surfaces here as "no working access yet" rather than a launch
        // failure - either way we just report false and let the caller back off and retry on
        // the next tick, no upper limit (2026-08-25).
        private static async Task<bool> EnsureBrowserOpen(AccountId accountId, IServiceScope scope, IChromeBrowser browser, CancellationToken cancellationToken)
        {
            var getAccessQuery = scope.ServiceProvider.GetRequiredService<GetValidAccessCommand.Handler>();
            var openBrowserCommand = scope.ServiceProvider.GetRequiredService<OpenBrowserCommand.Handler>();

            var accessResult = await getAccessQuery.HandleAsync(new(accountId), cancellationToken);
            if (accessResult.IsFailed)
            {
                var message = string.Join(' ', accessResult.Errors.Select(e => e.Message));
                browser.Logger.Warning("Browser is closed and no working access is available yet ({Message}), will keep trying.", message);
                return false;
            }

            try
            {
                await openBrowserCommand.HandleAsync(new(accountId, accessResult.Value), cancellationToken);
                browser.Logger.Information("Browser was closed - reopened it automatically.");
                return true;
            }
            catch (Exception ex)
            {
                browser.Logger.Warning("Could not reopen browser yet ({Message}), will keep trying.", ex.Message);
                return false;
            }
        }

        private async Task NotifyPaused(AccountId accountId, IServiceScope scope, string reason)
        {
            try
            {
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var setting = _telegramNotifier.Get(accountId);
                if (!setting.NotifyOnPause) return;

                var username = context.Accounts.FirstOrDefault(x => x.Id == accountId.Value)?.Username ?? $"{accountId}";
                var text = string.IsNullOrWhiteSpace(reason)
                    ? $"\u26D4 {username} duraklatildi, kontrol gerekiyor."
                    : $"\u26D4 {username} duraklatildi: {reason}";

                await _telegramNotifier.NotifyAsync(accountId, text);
            }
            catch
            {
                // notification is best-effort, never let it break the bot loop
            }
        }

        public void Shutdown()
        {
            _isShutdown = true;
            foreach (var timer in _timers.Values)
            {
                timer.Dispose();
            }
        }

        public void Start(AccountId accountId)
        {
            if (!_timers.ContainsKey(accountId))
            {
                var timer = new Timer(100) { AutoReset = false };
                timer.Elapsed += async (sender, e) =>
                {
                    if (_isShutdown) return;
                    await Execute(accountId);
                    timer.Start();
                };

                _timers.Add(accountId, timer);
                timer.Start();
            }
        }

        public record ContextData(string TaskName, IChromeBrowser Browser);
    }
}