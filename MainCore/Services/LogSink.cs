using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;

namespace MainCore.Services
{
    [RegisterSingleton<LogSink>]
    public sealed class LogSink : ILogEventSink
    {
        private Dictionary<AccountId, LinkedList<LogEvent>> Logs { get; } = [];

        private readonly IRxQueue _rxQueue;

        public LogSink(IRxQueue rxQueue)
        {
            _rxQueue = rxQueue;
        }

        // 2026-11-14: one lock for the dictionary and the lists. Readers (Debug tab, LogService) get a COPY, so enumerating
        // it while Emit adds a line no longer throws "Collection was modified" (TODO P2 "LogSink is not thread-safe").
        private readonly object _gate = new();

        public LinkedList<LogEvent> GetLogs(AccountId accountId)
        {
            lock (_gate)
            {
                return new LinkedList<LogEvent>(GetOrCreate(accountId));
            }
        }

        private LinkedList<LogEvent> GetOrCreate(AccountId accountId)
        {
            var logs = Logs.GetValueOrDefault(accountId);
            if (logs is null)
            {
                logs = new LinkedList<LogEvent>();
                Logs.Add(accountId, logs);
            }
            return logs;
        }

        public void Emit(LogEvent logEvent)
        {
            if (logEvent.Level < LogEventLevel.Information) return;
            var logEventPropertyValue = logEvent.Properties.GetValueOrDefault("AccountId");
            if (logEventPropertyValue is null) return;
            if (logEventPropertyValue is not ScalarValue scalarValue) return;
            var value = scalarValue.Value as string;
            var accountId = new AccountId(int.Parse(value!));

            lock (_gate)
            {
                var logs = GetOrCreate(accountId);
                logs.AddFirst(logEvent);
                // keeps 200 message
                if (logs.Count > 200)
                {
                    logs.RemoveLast();
                }
            }

            _rxQueue.Enqueue(new LogEmitted(accountId, logEvent));
        }
    }

    public static class LogSinkExtensions
    {
        public static LoggerConfiguration LogSink(
                  this LoggerSinkConfiguration loggerConfiguration)
        {
            return loggerConfiguration.Sink(Locator.Current.GetService<LogSink>()!);
        }
    }
}