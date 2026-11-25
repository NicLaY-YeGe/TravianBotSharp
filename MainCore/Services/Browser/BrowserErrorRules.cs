using System;
using System.Linq;

namespace MainCore.Services
{
    // 2026-10-28 (live log 10:33-12:09, TS-10X101/102): five "Browser was closed mid-task" events, each followed by a
    // re-open and a full re-login. All five came one second after a task started, right after the cookie-consent
    // step. AccountTaskBehavior closed the browser on ANY BrowserException, and browser.Html throws one whenever the
    // page is in the middle of a navigation ("Unable to retrieve content because the page is navigating and
    // changing the content"). A healthy browser was therefore closed because a page was loading.
    //
    // A "transient" message means the browser is fine and the very next read will work: wait a moment and read again.
    // Anything else keeps the old behaviour (treat the browser as gone, relaunch it).
    // Pure (no Playwright types) so it is unit tested.
    public static class BrowserErrorRules
    {
        private static readonly string[] TransientMarkers =
        [
            "page is navigating",
            "execution context was destroyed",
            "frame was detached",
            "most likely because of a navigation",
        ];

        // Words that mean the browser really is gone win over a transient marker in the same message.
        private static readonly string[] GoneMarkers =
        [
            "has been closed",
            "target closed",
            "browser closed",
            "browser has been closed",
            "connection closed",
            "page is not available",
        ];

        // 2026-10-29: this many transient failures in a row (one per task start, 10 s apart = about a minute) mean the page
        // is stuck, not loading: AccountTaskBehavior then treats the browser as closed (re-open + re-login) as before 2026-10-28.
        public const int MaxConsecutiveTransient = 6;

        public static bool ShouldGiveUp(int consecutiveTransientFailures) => consecutiveTransientFailures >= MaxConsecutiveTransient;

        public static bool IsTransient(string? message)
        {
            if (string.IsNullOrWhiteSpace(message)) return false;
            if (GoneMarkers.Any(x => message.Contains(x, StringComparison.OrdinalIgnoreCase))) return false;
            return TransientMarkers.Any(x => message.Contains(x, StringComparison.OrdinalIgnoreCase));
        }
    }
}
