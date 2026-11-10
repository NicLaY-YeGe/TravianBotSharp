namespace MainCore.Commands.NextExecute
{
    [Handler]
    public static partial class NextExecuteStartAdventureTaskCommand
    {
        public sealed record Command(StartAdventureTask.Task Task) : ICommand;

        private static async ValueTask HandleAsync(
            Command command,
            IChromeBrowser browser,
            ILogger logger,
            CancellationToken cancellationToken
            )
        {
            // 2026-10-25 (live log 16:55:59): this ran right after the click, before the page showed
            // the hero-adventure timer. Duration read as 0 -> ExecuteAt = now -> the task ran again 5 s
            // later, found no adventure, and was removed. Wait briefly for the timer, and never accept 0.
            var adventureDuration = TimeSpan.Zero;
            for (var i = 0; i < 12; i++)
            {
                adventureDuration = AdventureParser.GetAdventureDuration(browser.Html);
                if (adventureDuration > TimeSpan.Zero) break;
                try { await Task.Delay(500, cancellationToken); }
                catch (OperationCanceledException) { break; }
            }

            if (adventureDuration <= TimeSpan.Zero)
            {
                var minutes = Random.Shared.Next(FallbackMinMinutes, FallbackMaxMinutes + 1);
                command.Task.ExecuteAt = DateTime.Now.AddMinutes(minutes);
                logger.Warning("Start adventure: could not read the adventure timer, next look at {Time:HH:mm}.", command.Task.ExecuteAt);
                return;
            }

            command.Task.ExecuteAt = DateTime.Now.Add(adventureDuration * 2);
        }

        public const int FallbackMinMinutes = 20;
        public const int FallbackMaxMinutes = 30;
    }
}