namespace MainCore.Commands.NextExecute
{
    [Handler]
    public static partial class NextExecuteTrapTaskCommand
    {
        public sealed record Command(TrapTask.Task Task) : ICommand;

        private static async ValueTask HandleAsync(
            Command command,
            ISettingService settingService
            )
        {
            await Task.CompletedTask;
            var seconds = settingService.ByName(
                command.Task.VillageId,
                VillageSettingEnums.TrapRepeatTimeMin,
                VillageSettingEnums.TrapRepeatTimeMax,
                60
            );

            command.Task.ExecuteAt = DateTime.Now.AddSeconds(seconds);
        }
    }
}
