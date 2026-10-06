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

            if (DailyQuestParser.GetCollectButton(browser.Html) is not null)
            {
                var (_, collectFailed, collectElement, _) = await browser.GetElement(doc => DailyQuestParser.GetCollectButton(doc), cancellationToken);
                if (!collectFailed)
                {
                    var collectResult = await browser.Click(collectElement, cancellationToken);
                    if (collectResult.IsSuccess)
                    {
                        var doneResult = await browser.Wait(driver =>
                        {
                            var doc = new HtmlDocument();
                            doc.LoadHtml(driver.PageSource);
                            return !DailyQuestParser.HasAchievedReward(doc) || DailyQuestParser.GetCollectButton(doc) is null;
                        }, cancellationToken);

                        if (doneResult.IsSuccess) logger.Information("Daily quest reward: collected.");
                        else logger.Warning("Daily quest reward: clicked collect but could not confirm it went through.");
                    }
                }
            }
            else
            {
                logger.Information("Daily quest reward: the indicator was lit but there is nothing to collect right now.");
            }

            await delayService.DelayClick(cancellationToken);

            var (_, closeFailed, closeElement, _) = await browser.GetElement(doc => DailyQuestParser.GetCloseButton(doc), cancellationToken);
            if (!closeFailed) await browser.Click(closeElement, cancellationToken);
        }
    }
}
