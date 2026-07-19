namespace MainCore.Commands.Features.SendResource
{
    [Handler]
    public static partial class ToSendResourcePageCommand
    {
        public sealed record Command(VillageId VillageId) : IVillageCommand;

        // Index of the "Send resources" tab inside the Marketplace building page.
        // Tab 0 is the overview (where the NPC "Exchange resources" dialog lives),
        // tab 1 is "Send resources". Verified against real page captures (TR + EN clients).
        private const int SendResourceTabIndex = 1;

        private static async ValueTask<Result> HandleAsync(
            Command command,
            ToDorfCommand.Handler toDorfCommand,
            UpdateBuildingCommand.Handler updateBuildingCommand,
            ToBuildingByTypeCommand.Handler toBuildingCommand,
            SwitchTabCommand.Handler switchTabCommand,
            CancellationToken cancellationToken)
        {
            var villageId = command.VillageId;

            var result = await toDorfCommand.HandleAsync(new(2), cancellationToken);
            if (result.IsFailed) return result;

            result = await updateBuildingCommand.HandleAsync(new(villageId), cancellationToken);
            if (result.IsFailed) return result;

            result = await toBuildingCommand.HandleAsync(new(villageId, BuildingEnums.Marketplace), cancellationToken);
            if (result.IsFailed) return result;

            result = await switchTabCommand.HandleAsync(new(SendResourceTabIndex), cancellationToken);
            if (result.IsFailed) return result;

            return Result.Ok();
        }
    }
}
