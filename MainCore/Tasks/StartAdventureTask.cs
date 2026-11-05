using MainCore.Commands.Features.StartAdventure;
using MainCore.Commands.NextExecute;
using MainCore.Tasks.Base;

namespace MainCore.Tasks
{
    [Handler]
    public static partial class StartAdventureTask
    {
        public sealed class Task : AccountTask
        {
            public Task(AccountId accountId) : base(accountId)
            {
            }

            protected override string TaskName => "Start adventure";

            public override bool CanStart(AppDbContext context)
            {
                var settingEnable = context.BooleanByName(AccountId, AccountSettingEnums.EnableAutoStartAdventure);
                if (!settingEnable) return false;

                return true;
            }
        }

        // 2026-11-04 (live log 10:03-10:18): the hero's home village had lost its Rally Point, the game greyed the Explore
        // button out, and the old flow waited 180 s for a Continue button that never came - four times (Retry), then the
        // account was PAUSED, which also switched off Attack check / Defend. Start adventure is a secondary task
        // (CLAUDE.md rule 4): the whole run now has a hard time budget and any failure other than a user pause or a dead
        // browser becomes "look again later" (rule 16: ExecuteAt is moved first).
        private const int BudgetSeconds = 90;
        private const int BlockedMinMinutes = 45;
        private const int BlockedMaxMinutes = 60;
        private const int FailedMinMinutes = 15;
        private const int FailedMaxMinutes = 25;

        private static async ValueTask<Result> HandleAsync(
            Task task,
            IChromeBrowser browser,
            CheckHeroHealthCommand.Handler checkHeroHealthCommand,
            ToAdventurePageCommand.Handler toAdventurePageCommand,
            ExploreAdventureCommand.Handler exploreAdventureCommand,
            NextExecuteStartAdventureTaskCommand.Handler nextExecuteStartAdventureTaskCommand,
            ISettingService settingService,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(BudgetSeconds));

            var result = await RunAsync(
                task, checkHeroHealthCommand, toAdventurePageCommand, exploreAdventureCommand,
                nextExecuteStartAdventureTaskCommand, settingService, logger, deadline.Token);
            if (result.IsSuccess) return result;

            // A user pause stays a pause; a dead browser / driver is the behaviors' business.
            if (cancellationToken.IsCancellationRequested) return result;
            if (result.HasError<BrowserClosed>() || result.HasError<Stop>()) return result;

            var timedOut = deadline.IsCancellationRequested;
            if (timedOut) await PageDump.SaveAsync(browser, logger, "adventure-timeout");

            var reasons = string.Join(" | ", result.Reasons.Select(x => x.Message).Where(x => !string.IsNullOrWhiteSpace(x)));
            if (timedOut) reasons = $"no answer within {BudgetSeconds}s" + (reasons.Length > 0 ? $" ({reasons})" : "");
            task.ExecuteAt = DateTime.Now.AddMinutes(Random.Shared.Next(FailedMinMinutes, FailedMaxMinutes + 1));
            logger.Warning("Start adventure: could not start an adventure ({Reasons}) - looking again at {Time:HH:mm}.", reasons, task.ExecuteAt);
            return Result.Ok();
        }

        private static async ValueTask<Result> RunAsync(
            Task task,
            CheckHeroHealthCommand.Handler checkHeroHealthCommand,
            ToAdventurePageCommand.Handler toAdventurePageCommand,
            ExploreAdventureCommand.Handler exploreAdventureCommand,
            NextExecuteStartAdventureTaskCommand.Handler nextExecuteStartAdventureTaskCommand,
            ISettingService settingService,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            Result result;

            // Only pay for the extra hero/attributes page load when the user actually
            // configured a minimum - 0 (default) means no restriction.
            var minHeroHealthPercent = settingService.ByName(task.AccountId, AccountSettingEnums.MinHeroHealthPercent);
            if (minHeroHealthPercent > 0)
            {
                // Shared, short-lived reading (HeroHealthReader): Oasis scout asks the same question.
                var (_, isHealthCheckFailed, healthPercent, healthErrors) = await HeroHealthReader.GetAsync(task.AccountId, checkHeroHealthCommand, cancellationToken);
                if (isHealthCheckFailed) return Result.Fail(healthErrors);

                if (HeroHealthGate.IsBelowMinimum(healthPercent, minHeroHealthPercent))
                {
                    // 2026-10-25, real bug (live log 12:07, never ran again): this used to return
                    // Skip.Error with ExecuteAt unchanged, and TimerManager REMOVES a task that ends
                    // that way - so one low reading ended adventures for good (until the next login /
                    // Restart), even after the hero was healthy again. Now the task is moved to a
                    // later time and kept; it simply looks again then.
                    task.ExecuteAt = DateTime.Now.AddMinutes(HeroHealthGate.RecheckMinutes);
                    logger.Information(
                        "Start adventure: hero health {HealthPercent}% is below the configured minimum {MinPercent}% - no adventure now, looking again at {Time:HH:mm}.",
                        healthPercent, minHeroHealthPercent, task.ExecuteAt);
                    return Result.Ok();
                }
            }

            result = await toAdventurePageCommand.HandleAsync(new(), cancellationToken);
            if (result.IsFailed) return result;
            result = await exploreAdventureCommand.HandleAsync(new(), cancellationToken);
            if (result.IsFailed)
            {
                // 2026-10-25 (live log 14:17 and 16:56): "No adventure available" is a Skip with
                // ExecuteAt unchanged, which TimerManager answers by REMOVING the task - adventures
                // then never came back. Keep the task and look again later (CLAUDE.md rule 16).
                if (result.HasError<Skip>())
                {
                    // "No adventure available" is the normal case; any other Skip is a reason the game gives
                    // (greyed Explore button, no Rally Point in the hero's home village ...): say it, wait longer.
                    var reason = string.Join(" | ", result.Reasons.Select(x => x.Message));
                    var noAdventure = result.Reasons.Any(x => x.Message.StartsWith("No adventure available", StringComparison.Ordinal));
                    if (noAdventure)
                    {
                        task.ExecuteAt = DateTime.Now.AddMinutes(Random.Shared.Next(20, 31));
                        logger.Information("Start adventure: nothing to start now, looking again at {Time:HH:mm}.", task.ExecuteAt);
                    }
                    else
                    {
                        task.ExecuteAt = DateTime.Now.AddMinutes(Random.Shared.Next(BlockedMinMinutes, BlockedMaxMinutes + 1));
                        logger.Warning("Start adventure: cannot start now ({Reason}) - looking again at {Time:HH:mm}.", reason, task.ExecuteAt);
                    }
                    return Result.Ok();
                }
                return result;
            }

            await nextExecuteStartAdventureTaskCommand.HandleAsync(new(task), cancellationToken);
            return Result.Ok();
        }
    }
}