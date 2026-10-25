namespace MainCore.Commands.Features.Scheduling
{
    // 2026-10-25: right after a task has started something with a game-side queue (a batch of troops,
    // a batch of traps) the page it holds may still be the OLD one - Click returns once the click is
    // dispatched, not when the server's answer has been rendered. This waits a few seconds, under its
    // own short limit (the browser's default wait is 180 s, CLAUDE.md rule 9), for the queue table to
    // show up and then reads its countdown, so the task can come back exactly when the queue ends.
    // Returns null when nothing readable showed up in time: the caller then keeps its normal repeat
    // time (one extra visit at worst). Not pure (it drives the browser), so it is not in the harness.
    public static class QueueWait
    {
        public const int WaitSeconds = 6;

        public static async Task<int?> ReadSecondsAfterActionAsync(IChromeBrowser browser, CancellationToken cancellationToken)
        {
            using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            waitCts.CancelAfter(TimeSpan.FromSeconds(WaitSeconds));

            var result = await browser.Wait(driver =>
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(driver.PageSource);
                return TrainQueueParser.GetSecondsRemaining(doc) is not null;
            }, waitCts.Token);

            // A user pause arrives as a cancelled wait too - let it surface as a pause, not as "nothing found".
            cancellationToken.ThrowIfCancellationRequested();

            if (result.IsFailed) return null;
            return TrainQueueParser.GetSecondsRemaining(browser.Html);
        }
    }
}
