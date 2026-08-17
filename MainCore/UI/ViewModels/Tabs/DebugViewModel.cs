using MainCore.Commands.Poc;
using MainCore.Services.Playwright;
using MainCore.UI.Models.Output;
using MainCore.UI.ViewModels.Abstract;
using Serilog.Events;
using Serilog.Templates;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;

namespace MainCore.UI.ViewModels.Tabs
{
    [RegisterSingleton<DebugViewModel>]
    public partial class DebugViewModel : AccountTabViewModelBase
    {
        private readonly LogSink _logSink;
        private readonly ITaskManager _taskManager;
        private readonly ICustomServiceScopeFactory _serviceScopeFactory;
        private static readonly ExpressionTemplate _template = new("{@t:HH:mm:ss} [{@l:u3}] {@m}\n{@x}");

        public ObservableCollection<TaskItem> Tasks { get; } = [];
        private LinkedList<LogEvent> _logEvents = [];

        [Reactive]
        private string _logs = "";

        [Reactive]
        private string _endpointAddress = "";

        [Reactive]
        private string _playwrightPocX = "";

        [Reactive]
        private string _playwrightPocY = "";

        [Reactive]
        private string _playwrightPocStatus = "Not run yet.";

        public DebugViewModel(LogSink logSink, ITaskManager taskManager, IRxQueue rxQueue, ICustomServiceScopeFactory serviceScopeFactory)
        {
            _logSink = logSink;
            _taskManager = taskManager;
            _serviceScopeFactory = serviceScopeFactory;

            LoadTaskCommand.Subscribe(items =>
            {
                if (Tasks.Count == 0)
                {
                    foreach (var input in items)
                    {
                        Tasks.Add(input);
                    }
                    return;
                }

                if (items.Count == 0)
                {
                    Tasks.Clear();
                    return;
                }

                for (var i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    if (i > Tasks.Count - 1)
                    {
                        Tasks.Add(item);
                        continue;
                    }

                    Tasks[i].CopyFrom(item);
                }

                while (Tasks.Count > items.Count)
                {
                    Tasks.RemoveAt(Tasks.Count - 1);
                }
            });

            LoadLogCommand.BindTo(this, vm => vm.Logs);
            ReloadLogCommand.BindTo(this, vm => vm.Logs);
            LoadEndpointAddressCommand.BindTo(this, vm => vm.EndpointAddress);

            rxQueue.GetObservable<LogEmitted>()
                .InvokeCommand(LogEmittedCommand);

            rxQueue.GetObservable<TasksModified>()
                .InvokeCommand(TasksModifiedCommand);

            LogEmittedCommand
                .Where(x => x)
                .Select(_ => Unit.Default)
                .Throttle(TimeSpan.FromMilliseconds(100), RxApp.TaskpoolScheduler)
                .ObserveOn(RxApp.TaskpoolScheduler)
                .InvokeCommand(ReloadLogCommand);

            TasksModifiedCommand
                .Where(x => x)
                .Select(_ => AccountId)
                .Throttle(TimeSpan.FromMilliseconds(100), RxApp.TaskpoolScheduler)
                .ObserveOn(RxApp.TaskpoolScheduler)
                .InvokeCommand(LoadTaskCommand);
        }

        [ReactiveCommand]
        private bool LogEmitted(LogEmitted notification)
        {
            if (!IsActive) return false;
            var (accountId, logEvent) = notification;
            if (accountId != AccountId) return false;

            _logEvents.AddFirst(logEvent);
            return true;
        }

        [ReactiveCommand]
        private bool TasksModified(TasksModified notification)
        {
            if (!IsActive) return false;
            var accountId = notification.AccountId;
            if (accountId != AccountId) return false;
            return true;
        }

        protected override async Task Load(AccountId accountId)
        {
            await LoadTaskCommand.Execute(accountId);
            await LoadLogCommand.Execute(accountId);
            await LoadEndpointAddressCommand.Execute(accountId);
        }

        [ReactiveCommand]
        private List<TaskItem> LoadTask(AccountId accountId)
        {
            var tasks = _taskManager
               .GetTaskList(accountId)
               .Select(x => new TaskItem(x))
               .ToList();
            return tasks;
        }

        [ReactiveCommand]
        private string LoadLog(AccountId accountId)
        {
            var logs = _logSink.GetLogs(accountId);
            using var sw = new StringWriter(new StringBuilder());
            _logEvents.Clear();
            foreach (var log in logs)
            {
                _template.Format(log, sw);
                _logEvents.AddFirst(log);
            }
            return sw.ToString();
        }

        [ReactiveCommand]
        private string ReloadLog()
        {
            using var sw = new StringWriter(new StringBuilder());
            foreach (var log in _logEvents)
            {
                _template.Format(log, sw);
            }
            return sw.ToString();
        }

        [ReactiveCommand]
        private string LoadEndpointAddress(AccountId accountId)
        {
            return "Address endpoint is disabled";
        }

        [ReactiveCommand]
        private void Left()
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://ko-fi.com/vinaghost",
                UseShellExecute = true
            });
        }

        [ReactiveCommand]
        private void Right()
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(AppContext.BaseDirectory, "logs"),
                UseShellExecute = true
            });
        }

        // ⚠️ PROOF OF CONCEPT ONLY (2026-08-15) - see MainCore/Services/Playwright and
        // MainCore/Commands/Poc. Launches a SEPARATE, Playwright-driven Chromium window (not
        // the account's real Selenium browser) pointed at this account's karte.php, and runs
        // the exact same map-jump + "Found new village" + settle-confirm logic as the real
        // FoundNewVillageCommand, reusing MapParser/SettleConfirmParser completely unmodified.
        // First run needs a manual login in the popped-up window - the profile folder persists
        // it after that. Entirely disconnected from the real bot's task pipeline: running this
        // cannot affect or be affected by the account's normal automation.
        [ReactiveCommand]
        private async Task RunPlaywrightPoc()
        {
            if (!int.TryParse(PlaywrightPocX, out var x) || !int.TryParse(PlaywrightPocY, out var y))
            {
                PlaywrightPocStatus = "Enter valid X/Y coordinates first.";
                return;
            }

            string server;
            using (var scope = _serviceScopeFactory.CreateScope(AccountId))
            {
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                server = context.Accounts.First(a => a.Id == AccountId.Value).Server;
            }

            PlaywrightPocStatus = "Launching Playwright browser (log in by hand if prompted)...";

            await using var browser = new PlaywrightBrowserPoc();
            try
            {
                await browser.StartAsync();

                var mapUrl = $"{server.TrimEnd('/')}/karte.php";
                var navResult = await browser.NavigateAsync(mapUrl);
                if (navResult.IsFailed)
                {
                    PlaywrightPocStatus = $"Navigate failed: {string.Join(' ', navResult.Errors.Select(e => e.Message))}";
                    return;
                }

                PlaywrightPocStatus = $"On map page. Entering ({x}|{y}) and clicking through...";

                var result = await FoundNewVillageCommandPoc.RunAsync(browser, x, y, CancellationToken.None);

                PlaywrightPocStatus = result.IsSuccess
                    ? $"SUCCESS - founded new village at ({x}|{y}) via Playwright."
                    : $"FAILED: {string.Join(' ', result.Errors.Select(e => e.Message))}";
            }
            catch (Exception ex)
            {
                PlaywrightPocStatus = $"Exception: {ex.Message}";
            }
        }
    }
}