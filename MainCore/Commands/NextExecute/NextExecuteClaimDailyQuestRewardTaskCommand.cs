namespace MainCore.Commands.NextExecute
{
    // Re-arms ClaimDailyQuestRewardTask: next look in a random 20-40 minutes. A look costs no
    // page load - the task only reads the already loaded page for the "!" indicator.
    [Handler]
    public static partial class NextExecuteClaimDailyQuestRewardTaskCommand
    {
        public sealed record Command(ClaimDailyQuestRewardTask.Task Task) : ICommand;

        private const int MinMinutes = 20;
        private const int MaxMinutes = 40;

        private static async ValueTask HandleAsync(Command command)
        {
            await Task.CompletedTask;
            var minutes = Random.Shared.Next(MinMinutes, MaxMinutes + 1);
            command.Task.ExecuteAt = DateTime.Now.AddMinutes(minutes);
        }
    }
}
