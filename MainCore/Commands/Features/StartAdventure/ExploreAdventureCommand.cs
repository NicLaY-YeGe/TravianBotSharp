#pragma warning disable S1172

namespace MainCore.Commands.Features.StartAdventure
{
    [Handler]
    public static partial class ExploreAdventureCommand
    {
        public sealed record Command : ICommand;

        private static async ValueTask<Result> HandleAsync(
            Command command,
            IChromeBrowser browser,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            if (!AdventureParser.CanStartAdventure(browser.Html)) return Skip.Error.WithError("No adventure available");

            var adventureButton = AdventureParser.GetAdventureButton(browser.Html);
            // 2026-11-04: Skip, not Retry (CLAUDE.md rule 4): a missing button must never hold the queue or pause the account.
            if (adventureButton is null) return Skip.Error.WithError("Failed to find the adventure button");

            logger.Information("Start adventure {Adventure}", AdventureParser.GetAdventureInfo(adventureButton));

            // 2026-11-04 (live log 10:03-10:18): the game greys the Explore button out (e.g. the hero's home village lost
            // its Rally Point) with a CSS class only, so the click would "work" and the bot then waited 180 s x 4 for the
            // Continue button until the account was paused. Check the state first and give the reason.
            if (AdventureParser.IsExploreDisabled(adventureButton))
            {
                var message = AdventureParser.GetBlockedMessage(browser.Html);
                await PageDump.SaveAsync(browser, logger, "adventure-blocked");
                return Skip.Error.WithError(string.IsNullOrEmpty(message)
                    ? "The Explore button is disabled by the game"
                    : $"The Explore button is disabled by the game: {message}");
            }

            var (_, isFailed, element, errors) = await browser.GetElement(By.XPath(adventureButton.StableXPath()), cancellationToken);
            if (isFailed) return Result.Fail(errors).WithError($"Failed to find adventure button [{adventureButton.StableXPath()}]");

            var result = await browser.Click(element, cancellationToken);
            if (result.IsFailed) return result;

            static bool ContinueShow(IWebDriver driver)
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(driver.PageSource);
                var continueButton = AdventureParser.GetContinueButton(doc);
                return continueButton is not null;
            }
            result = await browser.Wait(ContinueShow, cancellationToken);
            if (result.IsFailed) return result;
            return Result.Ok();
        }
    }
}