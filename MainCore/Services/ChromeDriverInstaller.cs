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
    [RegisterSingleton<IChromeDriverInstaller, ChromeDriverInstaller>]
    public sealed class ChromeDriverInstaller(ILogger logger) : IChromeDriverInstaller
    {
        private readonly ILogger _logger = logger.ForContext<ChromeDriverInstaller>();

        public async Task Install()
        {
            _logger.Information("Checking the Playwright Chromium installation (downloads it on first run).");

            var exitCode = await Task.Run(() => Microsoft.Playwright.Program.Main(["install", "chromium"]));
            if (exitCode != 0)
            {
                throw new Exception($"Installing Playwright Chromium failed (exit code {exitCode}). Check the internet connection and restart the bot.");
            }

            _logger.Information("Playwright Chromium is ready.");
        }
    }
}
