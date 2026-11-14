namespace MainCore.Services
{
    // 2026-11-14 (live: the whole app died at 15:55:17 mid-task and left nothing in the log - there was no global
    // exception handler): every exception that would otherwise vanish with the process is written to Logs/crash.log
    // (plain file, independent of Serilog so it works even when logging itself is the problem) and, best effort, to Serilog.
    public static class CrashLog
    {
        private static readonly object Gate = new();
        private static bool _installed;

        public static void Write(string source, Exception? exception)
        {
            try
            {
                lock (Gate)
                {
                    var dir = Path.Combine(AppContext.BaseDirectory, "Logs");
                    Directory.CreateDirectory(dir);
                    File.AppendAllText(
                        Path.Combine(dir, "crash.log"),
                        $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
                }
            }
            catch
            {
                // nothing more can be done
            }

            try
            {
                Serilog.Log.Error(exception, "{Source}", source);
                Serilog.Log.CloseAndFlush();
            }
            catch
            {
                // logging must never throw from here
            }
        }

        // Process-wide handlers (idempotent). The first one is the last word of a dying process; the second sees tasks
        // whose exception nobody observed (it is marked observed so it cannot take the process down).
        public static void Install()
        {
            if (_installed) return;
            _installed = true;

            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                Write($"Unhandled exception (terminating: {e.IsTerminating})", e.ExceptionObject as Exception);

            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                Write("Unobserved task exception", e.Exception);
                e.SetObserved();
            };
        }
    }
}
