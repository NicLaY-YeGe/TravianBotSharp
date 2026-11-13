using MainCore.Commands.NextExecute;
using MainCore.Tasks.Base;

namespace MainCore.Tasks
{
    // 2026-10-24, user request: with the per-village auto refresh switched off the bot would not
    // look at the village list for long stretches, and a village that is attacked is only found
    // when some task happens to load a page. This task is the lightweight replacement for that
    // one job: every 4-6 minutes (random, AttackCheckRules) it loads a fresh village overview
    // (dorf1) and does nothing else.
    //
    // The detection itself is NOT in this file: AccountTaskBehavior runs UpdateVillageListCommand
    // right after every account task, which reads the attack flag of EVERY village off the village
    // list on the page (one page for all villages) and, for a village that just became attacked,
    // queues DodgeTroopTask + DefendDonateTask. So "attack found -> do what is needed" is the
    // existing defensive chain, and "no attack -> wait until the next check" is this task
    // re-arming itself. Auto refresh (UpdateVillageTask) is untouched.
    //
    // Defensive task, so it never returns Retry/Stop (CLAUDE.md rule 4): a failed page load is
    // logged and the next check is scheduled as usual. Only a closed browser or a user pause is
    // passed through, exactly as the rest of the bot treats them.
    [Handler]
    public static partial class AttackCheckTask
    {
        public sealed class Task : AccountTask
        {
            public Task(AccountId accountId) : base(accountId)
            {
            }

            protected override string TaskName => "Attack check";

            public override bool CanStart(AppDbContext context)
            {
                return context.BooleanByName(AccountId, AccountSettingEnums.EnableAttackCheck);
            }
        }

        private static async ValueTask<Result> HandleAsync(
            Task task,
            IChromeBrowser browser,
            ToDorfCommand.Handler toDorfCommand,
            NextExecuteAttackCheckTaskCommand.Handler nextExecuteAttackCheckTaskCommand,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            // ToDorfCommand does nothing when the browser is already on the requested dorf page, and
            // a page that is already open would show the village list as it was when it loaded -
            // so a page that is already dorf1 is reloaded explicitly. dorf pages are plain GET pages,
            // a reload cannot re-submit anything.
            var wasOnDorf1 = browser.CurrentUrl.Contains("dorf1");

            var result = await toDorfCommand.HandleAsync(new(1), cancellationToken);
            if (result.IsSuccess && wasOnDorf1)
            {
                result = await browser.Refresh(cancellationToken);
            }

            if (result.IsFailed)
            {
                if (result.HasError<BrowserClosed>() || result.HasError<Cancel>())
                {
                    return result;
                }

                logger.Warning("Attack check: could not load the village overview ({Error}) - trying again at the next check.", result.Errors.FirstOrDefault()?.Message);
                await nextExecuteAttackCheckTaskCommand.HandleAsync(new(task), cancellationToken);
                return Result.Ok();
            }

            // For the log only - the flag is stored and the defensive tasks are queued by
            // AccountTaskBehavior (see the header comment).
            var attacked = VillagePanelParser.Get(browser.Html).Where(x => x.IsUnderAttack).Select(x => x.Name).ToList();
            if (attacked.Count == 0)
            {
                logger.Information("Attack check: no village is under attack.");
            }
            else
            {
                logger.Information("Attack check: {Count} village(s) flagged as under attack: {Villages}.", attacked.Count, string.Join(", ", attacked));
            }

            await nextExecuteAttackCheckTaskCommand.HandleAsync(new(task), cancellationToken);
            return Result.Ok();
        }
    }
}
