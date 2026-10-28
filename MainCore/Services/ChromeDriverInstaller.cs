namespace MainCore.Services
{
    // 2026-10-18: Selenium -> Playwright. The name is kept so MainViewModel and the DI registration
    // did not change, but there is no chromedriver any more: this makes sure Playwright's own
    // Chromium build (the one that still loads the bundled extensions) is installed.
    //
    // "playwright install chromium" is idempotent: it returns quickly when the browser matching
    // this Microsoft.Playwright version is already there, and downloads it (~170 MB, first run
    // and after a Microsoft.Playwright version bump) otherwise. Default location:
    // %USERPROFILE%\AppData\Local\ms-playwright (override with PLAYWRIGHT_BROWSERS_PATH).
    //
    // 2026-10-20: first live run - the app closed ~5 s after "installing browser" because an
    // exception from the install escaped Load() and killed the process. The install is now
    // best-effort: whatever happens is written to the log together with the installer's own
    // console output, and the app starts anyway (opening a browser later reports the problem
    // again, per account, instead of the whole bot dying at startup).
    [RegisterSingleton<IChromeDriverInstaller, ChromeDriverInstaller>]
    public sealed class ChromeDriverInstaller(ILogger logger) : IChromeDriverInstaller
    {
        private readonly ILogger _logger = logger.ForContext<ChromeDriverInstaller>();

        public async Task Install()
        {
            _logger.Information("Checking the Playwright Chromium installation (downloads it on first run). Base directory: {BaseDirectory}", AppContext.BaseDirectory);

            var driverFolder = Path.Combine(AppContext.BaseDirectory, ".playwright");
            _logger.Information("Playwright driver folder {Folder} exists: {Exists}", driverFolder, Directory.Exists(driverFolder));

            var originalOut = Console.Out;
            var originalError = Console.Error;
            var captured = new StringWriter();

            try
            {
                Console.SetOut(captured);
                Console.SetError(captured);

                var exitCode = await Task.Run(() => Microsoft.Playwright.Program.Main(["install", "chromium"]));

                Console.SetOut(originalOut);
                Console.SetError(originalError);

                if (exitCode == 0)
                {
                    _logger.Information("Playwright Chromium is ready.");
                }
                else
                {
                    _logger.Error("Installing Playwright Chromium failed (exit code {ExitCode}). Installer output: {Output}", exitCode, Last(captured));
                }
            }
            catch (Exception ex)
            {
                Console.SetOut(originalOut);
                Console.SetError(originalError);
                _logger.Error(ex, "Installing Playwright Chromium threw {Type}: {Message}. Installer output: {Output}", ex.GetType().Name, ex.Message, Last(captured));
            }
        }

        private static string Last(StringWriter writer)
        {
            var text = writer.ToString().Trim();
            return text.Length <= 3000 ? text : text[^3000..];
        }
    }
}
