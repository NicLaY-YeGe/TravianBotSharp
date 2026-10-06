using MainCore.Commands.NextExecute;
using MainCore.Tasks.Base;

namespace MainCore.Tasks
{
    // 2026-10-06, user request: collects the Daily Quests rewards automatically. Every 20-40
    // minutes (NextExecuteClaimDailyQuestRewardTaskCommand) it looks at the page the browser is
    // already on; only when the top-bar daily-quests icon shows its "!" indicator does it open
    // the dialog, click "Collect rewards" and close the dialog again. A convenience task: every
    // "could not find / did not confirm" outcome is logged and re-armed, never returned as a
    // failure, so it can never pause the bot (only cancellation propagates).
    [Handler]
    public static partial class ClaimDailyQuestRewardTask
    {
        public sealed class Task : AccountTask
        {
            public Task(AccountId accountId) : base(accountId)
            {
            }

            protected override string TaskName => "Claim daily quest reward";

            public override bool CanStart(AppDbContext context)
            {
                return context.BooleanByName(AccountId, AccountSettingEnums.EnableClaimDailyQuestReward);
            }
        }

        private static async ValueTask<Result> HandleAsync(
            Task task,
            IChromeBrowser browser,
            IDelayService delayService,
            NextExecuteClaimDailyQuestRewardTaskCommand.Handler nextExecuteCommand,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            try
            {
                await ClaimIfAvailable(browser, delayService, logger, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                logger.Warning(e, "Daily quest reward: unexpected problem - leaving it for the next look.");
            }

            await nextExecuteCommand.HandleAsync(new(task), cancellationToken);
            return Result.Ok();
        }

        private static async System.Threading.Tasks.Task ClaimIfAvailable(
            IChromeBrowser browser,
            IDelayService delayService,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            if (!DailyQuestParser.IsClaimable(browser.Html)) return;

            var (_, openFailed, openElement, _) = await browser.GetElement(doc => DailyQuestParser.GetOpenLink(doc), cancellationToken);
            if (openFailed) return;

            var clickResult = await browser.Click(openElement, cancellationToken);
            if (clickResult.IsFailed) return;

            var openedResult = await browser.Wait(driver =>
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(driver.PageSource);
                return DailyQuestParser.IsDialogOpen(doc);
            }, cancellationToken);
            if (openedResult.IsFailed)
            {
                logger.Warning("Daily quest reward: the dialog did not open.");
                return;
            }

            await delayService.DelayClick(cancellationToken);

            // 2026-10-06 live capture: "Collect rewards" only opens a per-milestone reward screen;
            // the reward is claimed by that screen's own "Collect" button, and "Back" returns to
            // the overview (one pass per unclaimed milestone). A small state machine, bounded by
            // MaxSteps, that always ends by closing the dialog - never a long browser.Wait (an
            // earlier version hung there until the task's time budget cancelled the run).
            var collected = 0;
            for (var step = 0; step < MaxSteps; step++)
            {
                var html = browser.Html;

                if (DailyQuestParser.GetRewardCollectButton(html) is not null)
                {
                    if (!await ClickNode(browser, doc => DailyQuestParser.GetRewardCollectButton(doc), cancellationToken)) break;
                    collected++;
                    logger.Information("Daily quest reward: collect clicked on the reward screen ({Count}).", collected);
                }
                else if (DailyQuestParser.IsRewardScreen(html))
                {
                    // Nothing (more) to collect on this screen: back to the overview.
                    if (!await ClickNode(browser, doc => DailyQuestParser.GetRewardBackButton(doc), cancellationToken)) break;
                }
                else if (DailyQuestParser.GetCollectButton(html) is not null)
                {
                    if (!await ClickNode(browser, doc => DailyQuestParser.GetCollectButton(doc), cancellationToken)) break;
                }
                else
                {
                    break; // overview with nothing collectable left
                }

                await delayService.DelayClick(cancellationToken);
                await System.Threading.Tasks.Task.Delay(1000, cancellationToken);
            }

            var end = browser.Html;
            logger.Information(
                "Daily quest reward: finished - collected={Collected}, rewardScreen={Screen}, achievedLeft={Achieved}, collectEnabled={Button}.",
                collected,
                DailyQuestParser.IsRewardScreen(end),
                DailyQuestParser.HasAchievedReward(end),
                DailyQuestParser.GetCollectButton(end) is not null);

            await delayService.DelayClick(cancellationToken);

            var (_, closeFailed, closeElement, _) = await browser.GetElement(doc => DailyQuestParser.GetCloseButton(doc), cancellationToken);
            if (!closeFailed) await browser.Click(closeElement, cancellationToken);
        }

        private const int MaxSteps = 12;

        private static async ValueTask<bool> ClickNode(
            IChromeBrowser browser,
            Func<HtmlDocument, HtmlNode?> node,
            CancellationToken cancellationToken)
        {
            var (_, failed, element, _) = await browser.GetElement(node, cancellationToken);
            if (failed) return false;

            var result = await browser.Click(element, cancellationToken);
            return result.IsSuccess;
        }
    }
}
