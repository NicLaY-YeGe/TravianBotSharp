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

        // 2026-10-19 (user decision): from 3-5 minutes before the impact until just after it, the
        // queue is reserved for the defensive tasks (see AttackReservationRules); every other
        // task is held back by TimerManager and runs after the attack. Called by Dodge and
        // Defend as soon as they have read the REAL impact time off the Rally Point.
        public static void Reserve(AccountId accountId, VillageId villageId, int attackSeconds, ILogger logger)
        {
            var now = DateTime.Now;
            var (reservation, isNew) = AttackReservationStore.Register(
                accountId.Value, villageId.Value, now.AddSeconds(attackSeconds), now, Random.Shared);

            if (isNew)
            {
                logger.Information(
                    "Attack on {VillageId} lands {Impact:HH:mm:ss}: other tasks wait from {From:HH:mm:ss} until {Until:HH:mm:ss} so Dodge is not blocked.",
                    villageId, reservation.ImpactAt, reservation.ReservedFrom, reservation.ReservedUntil);
            }
        }

        // The attack is gone (landed, cancelled, or the under-attack flag was stale).
        public static void ReleaseReservation(AccountId accountId, VillageId villageId)
            => AttackReservationStore.Clear(accountId.Value, villageId.Value);
    }
}
