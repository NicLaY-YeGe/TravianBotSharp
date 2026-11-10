using MainCore.Commands.Features.AttackCheck;

namespace MainCore.Commands.NextExecute
{
    // Re-arms AttackCheckTask: next look in a random 4-6 minutes (AttackCheckRules).
    [Handler]
    public static partial class NextExecuteAttackCheckTaskCommand
    {
        public sealed record Command(AttackCheckTask.Task Task) : ICommand;

        private static async ValueTask HandleAsync(Command command)
        {
            await Task.CompletedTask;
            command.Task.ExecuteAt = DateTime.Now.Add(AttackCheckRules.NextDelay(Random.Shared));
        }
    }
}
