namespace MainCore.Commands.NextExecute
{
    [Handler]
    public static partial class NextExecuteCropOasisScanTaskCommand
    {
        public sealed record Command(CropOasisScanTask.Task Task) : ICommand;

        private static async ValueTask HandleAsync(
            Command command,
            ISettingService settingService
            )
        {
            await Task.CompletedTask;
            var seconds = settingService.ByName(
                command.Task.VillageId,
                VillageSettingEnums.CropScanGapMinSeconds,
                VillageSettingEnums.CropScanGapMaxSeconds,
                1
            );

            command.Task.ExecuteAt = DateTime.Now.AddSeconds(seconds);
        }
    }
}
