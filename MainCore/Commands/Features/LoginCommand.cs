namespace MainCore.Commands.Features
{
    [Handler]
    public static partial class LoginCommand
    {
        public sealed record Command(AccountId AccountId) : IAccountCommand;

        private static async ValueTask<Result> HandleAsync(
            Command command,
            IChromeBrowser browser,
            AppDbContext context,
            CancellationToken cancellationToken)
        {
            if (LoginParser.IsIngamePage(browser.Html)) return Result.Ok();

            var (username, password) = GetLoginInfo(command.AccountId, context);

            Result result;

            // Username/password/login-button used to be located via a positional XPath
            // computed by HtmlAgilityPack over driver.PageSource, then re-queried against
            // the live DOM through Selenium. That breaks on pages with an inline SVG before
            // the form (e.g. this server's SVG logo): HtmlAgilityPack parses SVG without
            // namespace-awareness, so its node count diverges from the real DOM's, the
            // computed XPath points at nothing, and GetElement spins for the full 3-minute
            // wait finding zero elements. Using native Selenium By locators here instead
            // queries the live DOM directly and never goes through HtmlAgilityPack, so it's
            // immune to that mismatch regardless of what markup sits before the form.
            var (_, isFailed, element, errors) = await browser.GetElement(By.Name("name"), cancellationToken);
            if (isFailed) return Result.Fail(errors);
            result = await browser.Input(element, username, cancellationToken);
            if (result.IsFailed) return result;

            (_, isFailed, element, errors) = await browser.GetElement(By.Name("password"), cancellationToken);
            if (isFailed) return Result.Fail(errors);
            result = await browser.Input(element, password, cancellationToken);
            if (result.IsFailed) return result;

            (_, isFailed, element, errors) = await browser.GetElement(By.XPath("//input[@name='password']/ancestor::form[1]//button[contains(@class,'green')]"), cancellationToken);
            if (isFailed) return Result.Fail(errors);
            result = await browser.Click(element, cancellationToken);
            if (result.IsFailed) return result;

            result = await browser.WaitPageChanged("dorf", cancellationToken);
            if (result.IsFailed) return result;

            return Result.Ok();
        }

        private static (string username, string password) GetLoginInfo(AccountId accountId, AppDbContext context)
        {
            var data = context.Accesses
                .Where(x => x.AccountId == accountId.Value)
                .OrderByDescending(x => x.LastUsed)
                .Select(x => new { x.Username, x.Password })
                .FirstOrDefault();

            if (data is null) return ("", "");

            return (data.Username, data.Password);
        }
    }
}