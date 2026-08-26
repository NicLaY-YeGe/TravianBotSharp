namespace MainCore.Errors
{
    // Distinct from Stop: means the whole Chrome process/window is gone (closed manually,
    // crashed, or the connection to it died) rather than an ordinary task failure - see
    // ChromeBrowser.RefreshContextAsync and TimerManager.Execute (2026-08-25 - user closed
    // Chrome by accident mid-run, bot never reopened it and eventually crashed instead of
    // pausing). TimerManager treats this specially: instead of pausing the account, it
    // leaves the failed task at the head of the queue and lets the next tick's browser-open
    // check relaunch Chrome and retry automatically - including waiting out a dropped
    // network connection instead of giving up after a few attempts.
    public class BrowserClosed : Error
    {
        private BrowserClosed() : base("Browser was closed")
        {
        }

        public static Result Error => new BrowserClosed();
    }
}
