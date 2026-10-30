namespace MainCore.Services
{
    // 2026-10-22: evidence for stalled or unconfirmed browser steps. A live log alone could not tell
    // "element not found" from "element hidden" from "click did nothing" (the 2026-09-27 alliance
    // donation hang, the 2026-10-21 troop-send hangs). Saves the page HTML and a screenshot next to
    // the exe (Screenshots/) and logs the paths; it never throws.
    public static class PageDump
    {
        public static async Task SaveAsync(IChromeBrowser browser, ILogger logger, string tag)
        {
            try
            {
                var screenshot = await browser.Screenshot();
                var dir = Path.Combine(AppContext.BaseDirectory, "Screenshots");
                Directory.CreateDirectory(dir);
                var htmlFile = Path.Combine(dir, $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}-{tag}.html");
                await File.WriteAllTextAsync(htmlFile, browser.Html.DocumentNode.OuterHtml);
                logger.Warning("{Tag}: page saved to {Html}, screenshot {Screenshot}, url {Url}.", tag, htmlFile, screenshot, browser.CurrentUrl);
            }
            catch (Exception ex)
            {
                logger.Warning("{Tag}: the page could not be saved: {Message}", tag, ex.Message);
            }
        }
    }
}
