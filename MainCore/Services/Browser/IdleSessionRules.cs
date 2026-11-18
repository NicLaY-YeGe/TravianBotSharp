using System.Collections.Concurrent;

namespace MainCore.Services
{
    // 2026-11-15 (live log 05:59 -> 10:00, 4 h without a task - offline hours / pause): the browser kept showing the old
    // in-game page, so AccountTaskBehavior's IsIngamePage check passed, but the server session had expired. The first task
    // clicked a village link (SwitchVillageCommand), the click redirected to the login page and the 180 s wait for
    // "village changed" ran out ("Timed out after 180 seconds / villageChanged", Retry, then "Account is logout. Re-login
    // now"). The same pattern was logged on 2026-11-02 10:00. After a long idle gap the page is reloaded BEFORE it is
    // judged, so a dead session shows the login page at once and the existing re-login path runs. Pure + memory only.
    public static class IdleSessionRules
    {
        public static readonly TimeSpan RefreshAfter = TimeSpan.FromMinutes(30);

        // lastActivity = end of the previous task of this account (null = none yet, e.g. right after the first login).
        public static bool ShouldRefresh(DateTime? lastActivity, DateTime now)
            => lastActivity is not null && now - lastActivity.Value >= RefreshAfter;
    }

    public static class AccountActivity
    {
        private static readonly ConcurrentDictionary<int, DateTime> LastActivity = new();

        public static void Touch(int accountId) => LastActivity[accountId] = DateTime.Now;

        public static DateTime? Get(int accountId) => LastActivity.TryGetValue(accountId, out var value) ? value : null;

        public static void Clear(int accountId) => LastActivity.TryRemove(accountId, out _);
    }
}
