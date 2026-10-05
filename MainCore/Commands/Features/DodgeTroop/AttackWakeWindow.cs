using MainCore.Commands.Features.SyncAttack;

namespace MainCore.Commands.Features.DodgeTroop
{
    // 2026-10-03, user request: "bana gelen bir saldırıyı tespit ederse ve hesabım saldırının
    // gerçekleşeceği zamanda uykudaysa/offline ise, 5-10 dk önce hesabı aktif hale getirsin ve
    // gerekeni (dodge, sığınak üzeri maddeleri kullanma) yapsın". Shared by DodgeTroopTask and
    // DefendDonateTask: each calls Apply() at the moment it learns (or re-learns) when its first
    // action is due and when the attack lands; the pure window math is WakeWindowRules.
    //
    // Limitation by design (user decision: no "sentinel" check-ins): the attack must have been
    // DETECTED while the bot was awake - a sleeping bot loads no pages and cannot see it.
    public static class AttackWakeWindow
    {
        // If the window would open within this long (or already has), the bot is awake and
        // working right now - a WakeUpTask (page reload + login check) would only waste time on
        // the path where every second counts.
        private static readonly TimeSpan SkipWakeUpTaskIfOpensWithin = TimeSpan.FromSeconds(30);

        public static void Apply(
            AppDbContext context,
            ITaskManager taskManager,
            AccountId accountId,
            DateTime firstActionAt,
            DateTime impactAt,
            Action<DateTime, DateTime> setWindow,
            ILogger logger)
        {
            if (!context.BooleanByName(accountId, AccountSettingEnums.EnableAttackWake)) return;

            var options = new WakeWindowOptions(
                context.ByName(accountId, AccountSettingEnums.AttackWakeBeforeMinMinutes),
                context.ByName(accountId, AccountSettingEnums.AttackWakeBeforeMaxMinutes),
                context.ByName(accountId, AccountSettingEnums.AttackWakeAfterMinMinutes),
                context.ByName(accountId, AccountSettingEnums.AttackWakeAfterMaxMinutes));

            var (start, end) = WakeWindowRules.ComputeAttackWindow(firstActionAt, impactAt, options, Random.Shared);
            setWindow(start, end);

            logger.Information(
                "Wake window for the incoming attack: {Start:yyyy-MM-dd HH:mm:ss} -> {End:yyyy-MM-dd HH:mm:ss} (first action {Action:HH:mm:ss}, impact {Impact:HH:mm:ss}).",
                start, end, firstActionAt, impactAt);

            if (start - DateTime.Now <= SkipWakeUpTaskIfOpensWithin) return;

            // One shared WakeUpTask per account: if one is already queued, only pull it EARLIER
            // when this window opens sooner (several attacked villages, or a re-computed window).
            var existing = taskManager.GetTaskList(accountId).ToArray().OfType<WakeUpTask.Task>().FirstOrDefault();
            if (existing is null)
            {
                taskManager.Add(new WakeUpTask.Task(accountId, start));
            }
            else if (start < existing.ExecuteAt)
            {
                existing.ExecuteAt = start;
                taskManager.ReOrder(accountId);
            }
        }
    }
}
