namespace MainCore.Commands.NextExecute
{
    // Re-arms RaidReportTask: the next run is a random 15-25 minutes away. Not user-configurable
    // on purpose - it is one page load per run (plus a report page for each NEW report to a
    // player village), and a fixed random window keeps the browsing pattern irregular without
    // yet another setting. Raid List intervals default to 30-60 minutes, so a bad row is
    // usually caught before its next send.
    [Handler]
    public static partial class NextExecuteRaidReportTaskCommand
    {
        public sealed record Command(RaidReportTask.Task Task) : ICommand;

        private const int MinMinutes = 15;
        private const int MaxMinutes = 25;

        private static async ValueTask HandleAsync(Command command)
        {
            await Task.CompletedTask;
            var minutes = Random.Shared.Next(MinMinutes, MaxMinutes + 1);
            command.Task.ExecuteAt = DateTime.Now.AddMinutes(minutes);
        }
    }
}
