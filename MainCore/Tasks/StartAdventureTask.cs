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

        private static async ValueTask<Result> HandleAsync(
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
                    task.ExecuteAt = DateTime.Now.AddMinutes(Random.Shared.Next(20, 31));
                    logger.Information("Start adventure: nothing to start now, looking again at {Time:HH:mm}.", task.ExecuteAt);
                    return Result.Ok();
                }
                return result;
            }

            await nextExecuteStartAdventureTaskCommand.HandleAsync(new(task), cancellationToken);
            return Result.Ok();
        }
    }
}