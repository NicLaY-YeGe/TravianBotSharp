#pragma warning disable S1172

namespace MainCore.Commands.Features.ClaimQuest
{
    [Handler]
    public static partial class ClaimQuestCommand
    {
        public sealed record Command : ICommand;

        // A full pass = every tab, every page of each tab. One pass is normally enough; a
        // second/third only matters if claiming something late in a pass reveals a new item
        // earlier in tab/page order (rare - chained quests). Hard-capped so a quest the parser
        // genuinely can't recognize can never turn this into the 2026-09-29 infinite loop this
        // rewrite fixes (see CHANGELOG) - if the bubble is still lit after MaxPasses, this logs
        // a warning and returns Ok anyway rather than spinning.
        private const int MaxPasses = 3;

        // 2026-09-29, real bug fixed here: the "new quest" bubble (QuestParser.IsQuestClaimable)
        // lights up for a claimable quest in EITHER of the quest master's two tabs ("this
        // village" / "General tasks") and on ANY page of a tab's paginated task list, but this
        // command used to only ever look at tab 1's currently-displayed page. A quest sitting
        // anywhere else was never reached, so it was never actually claimed - the bubble never
        // cleared, UpdateQuestCommand kept re-adding ClaimQuestTask every time it ran, and the
        // bot spun on "Claim quest" every few seconds forever. See CHANGELOG for the report.
        private static async ValueTask<Result> HandleAsync(
            Command command,
            IChromeBrowser browser,
            IDelayService delayService,
            SwitchTabCommand.Handler switchTabCommand,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            for (var pass = 0; pass < MaxPasses; pass++)
            {
                if (cancellationToken.IsCancellationRequested) return Cancel.Error;

                var claimedThisPass = false;
                var tabCount = Math.Max(BuildingTabParser.CountTab(browser.Html), 1);

                for (var tabIndex = 0; tabIndex < tabCount; tabIndex++)
                {
                    if (cancellationToken.IsCancellationRequested) return Cancel.Error;

                    // A tab that no longer exists (page layout changed under us) is skipped, not
                    // fatal - the other tab(s) may still hold the claimable quest.
                    var switchResult = await switchTabCommand.HandleAsync(new(tabIndex), cancellationToken);
                    if (switchResult.IsFailed) continue;

                    await delayService.DelayClick(cancellationToken);

                    var claimedInTab = await ClaimEveryPageInCurrentTab(browser, delayService, cancellationToken);
                    if (claimedInTab.IsFailed) return Result.Fail(claimedInTab.Errors);
                    if (claimedInTab.Value) claimedThisPass = true;
                }

                if (!QuestParser.IsQuestClaimable(browser.Html)) return Result.Ok();
                if (!claimedThisPass) break; // a full pass found nothing, yet the bubble is lit - stop, see below
            }

            logger.Warning("Claim quest: the quest bubble is still lit after checking every tab and page - leaving it rather than looping.");
            return Result.Ok();
        }

        // Claims every collectible quest on every pagination page of whichever tab is currently
        // active, advancing to the next page only once the current one has nothing left. Returns
        // whether anything was claimed here.
        private static async ValueTask<Result<bool>> ClaimEveryPageInCurrentTab(
            IChromeBrowser browser,
            IDelayService delayService,
            CancellationToken cancellationToken)
        {
            var claimedAny = false;

            while (true)
            {
                if (cancellationToken.IsCancellationRequested) return Result.Fail(Cancel.Error);

                var quest = QuestParser.GetQuestCollectButton(browser.Html);
                if (quest is not null)
                {
                    var (_, isFailed, element, errors) = await browser.GetElement(By.XPath(quest.XPath), cancellationToken);
                    if (isFailed) return Result.Fail(errors);

                    var clickResult = await browser.Click(element, cancellationToken);
                    if (clickResult.IsFailed) return clickResult;

                    await delayService.DelayClick(cancellationToken);
                    claimedAny = true;
                    continue; // more may have appeared on this same page
                }

                var nextPage = QuestParser.GetNextPageButton(browser.Html);
                if (nextPage is null) return claimedAny; // last page of this tab

                var (_, pageFailed, pageElement, pageErrors) = await browser.GetElement(By.XPath(nextPage.XPath), cancellationToken);
                if (pageFailed) return Result.Fail(pageErrors);

                var pageClickResult = await browser.Click(pageElement, cancellationToken);
                if (pageClickResult.IsFailed) return pageClickResult;

                await delayService.DelayClick(cancellationToken);
            }
        }
    }
}
