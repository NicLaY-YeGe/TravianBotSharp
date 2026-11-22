namespace MainCore.Commands.Features.StartAdventure
{
    // 2026-10-25: reads the hero's health for the tasks that must not send a hurt or dead hero
    // anywhere (StartAdventureTask, OasisScoutTask). It goes through HeroHealthGate's short-lived
    // cache, so two tasks that run a few minutes apart (or the four Oasis scout villages of one
    // account) cost one /hero/attributes page load, not one each. Not pure (it needs the generated
    // command handler), so it is kept out of the pure-test harness; the decisions it feeds are in
    // HeroHealthGate, which is tested.
    public static class HeroHealthReader
    {
        public static async ValueTask<Result<int>> GetAsync(
            AccountId accountId,
            CheckHeroHealthCommand.Handler checkHeroHealthCommand,
            CancellationToken cancellationToken)
        {
            if (HeroHealthGate.TryGetFresh(accountId.Value, DateTime.Now, out var cachedPercent))
            {
                return Result.Ok(cachedPercent);
            }

            var result = await checkHeroHealthCommand.HandleAsync(new(), cancellationToken);
            if (result.IsSuccess)
            {
                HeroHealthGate.Record(accountId.Value, result.Value, DateTime.Now);
            }

            return result;
        }
    }
}
