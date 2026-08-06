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
            CancellationToken cancellationToken)
        {
            Result result;

            // Only pay for the extra hero/attributes page load when the user actually
            // configured a minimum - 0 (default) means no restriction.
            var minHeroHealthPercent = settingService.ByName(task.AccountId, AccountSettingEnums.MinHeroHealthPercent);
            if (minHeroHealthPercent > 0)
            {
                var (_, isHealthCheckFailed, healthPercent, healthErrors) = await checkHeroHealthCommand.HandleAsync(new(), cancellationToken);
                if (isHealthCheckFailed) return Result.Fail(healthErrors);

                if (healthPercent < minHeroHealthPercent)
                {
                    return Skip.Error.WithError($"Hero health {healthPercent}% is below the configured minimum {minHeroHealthPercent}%. Not sending hero on adventure.");
                }
            }

            result = await toAdventurePageCommand.HandleAsync(new(), cancellationToken);
            if (result.IsFailed) return result;
            result = await exploreAdventureCommand.HandleAsync(new(), cancellationToken);
            if (result.IsFailed) return result;

            await nextExecuteStartAdventureTaskCommand.HandleAsync(new(task), cancellationToken);
            return Result.Ok();
        }
    }
}