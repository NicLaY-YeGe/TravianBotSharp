namespace MainCore.Commands.NextExecute
{
    [Handler]
    public static partial class NextExecuteOasisScoutTaskCommand
    {
        public sealed record Command(OasisScoutTask.Task Task) : ICommand;

        private static async ValueTask HandleAsync(
            Command command,
            ISettingService settingService
            )
        {
            await Task.CompletedTask;
            var minutes = settingService.ByName(
                command.Task.VillageId,
                VillageSettingEnums.OasisScoutIntervalMin,
                VillageSettingEnums.OasisScoutIntervalMax,
                1);
            command.Task.ExecuteAt = DateTime.Now.AddMinutes(Math.Max(1, minutes));
        }
    }
}
