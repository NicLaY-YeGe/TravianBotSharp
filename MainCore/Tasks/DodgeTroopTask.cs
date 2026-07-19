using MainCore.Commands.Features.DodgeTroop;
using MainCore.Tasks.Base;

namespace MainCore.Tasks
{
    [Handler]
    public static partial class DodgeTroopTask
    {
        // Runs on the village that's under attack. Sends the configured troop slot's full
        // stack to the nearest own village as reinforcement.
        public sealed class Task : VillageTask
        {
            public Task(AccountId accountId, VillageId villageId) : base(accountId, villageId)
            {
            }

            protected override string TaskName => "Dodge troops";

            public override bool CanStart(AppDbContext context)
            {
                var enabled = context.BooleanByName(VillageId, VillageSettingEnums.DodgeEnable);
                if (!enabled) return false;

                var village = context.Villages.FirstOrDefault(x => x.Id == VillageId.Value);
                if (village is null || !village.IsUnderAttack) return false;

                var target = GetNearestVillage(context, AccountId, VillageId);
                return target is not null;
            }
        }

        // Nearest own village by straight-line distance. Whether it can actually be reached
        // in time is not checked here - per the chosen behaviour, we dodge anyway even if late.
        public static VillageId? GetNearestVillage(AppDbContext context, AccountId accountId, VillageId sourceVillageId)
        {
            var source = context.Villages.FirstOrDefault(x => x.Id == sourceVillageId.Value);
            if (source is null) return null;

            var candidates = context.Villages
                .Where(x => x.AccountId == accountId.Value)
                .Where(x => x.Id != sourceVillageId.Value)
                .ToList();

            if (candidates.Count == 0) return null;

            var nearest = candidates
                .OrderBy(x => DistanceSquared(source.X, source.Y, x.X, x.Y))
                .First();

            return new VillageId(nearest.Id);
        }

        private static long DistanceSquared(int x1, int y1, int x2, int y2)
        {
            var dx = (long)(x1 - x2);
            var dy = (long)(y1 - y2);
            return (dx * dx) + (dy * dy);
        }

        private static async ValueTask<Result> HandleAsync(
            Task task,
            AppDbContext context,
            IChromeBrowser browser,
            ToRallyPointOverviewCommand.Handler toRallyPointOverviewCommand,
            ToSendTroopsPageCommand.Handler toSendTroopsPageCommand,
            SendReinforcementCommand.Handler sendReinforcementCommand,
            ITaskManager taskManager,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            var target = GetNearestVillage(context, task.AccountId, task.VillageId);
            if (target is null)
            {
                // no other village to dodge to
                return Skip.Error;
            }

            var troopSlot = context.ByName(task.VillageId, VillageSettingEnums.DodgeTroopSlot);
            if (troopSlot <= 0) troopSlot = 1;

            // Read the incoming attack's own arrival time first (from the Overview tab) so the
            // recall can be scheduled off when the ATTACK lands, not off our own dodge arrival.
            DateTime? attackArrivalTime = null;
            var overviewResult = await toRallyPointOverviewCommand.HandleAsync(new(task.VillageId), cancellationToken);
            if (overviewResult.IsFailed)
            {
                if (overviewResult.HasError<MissingBuilding>())
                {
                    logger.Warning("No rally point in this village, cannot dodge.");
                    return Skip.Error.WithErrors(overviewResult.Errors);
                }
                return overviewResult;
            }

            var attackSeconds = RallyPointOverviewParser.GetIncomingAttackSeconds(browser.Html);
            if (attackSeconds is not null)
            {
                attackArrivalTime = DateTime.Now.AddSeconds(attackSeconds.Value);
                logger.Information("Incoming attack lands in {Seconds}s.", attackSeconds.Value);
            }

            var result = await toSendTroopsPageCommand.HandleAsync(new(task.VillageId), cancellationToken);
            if (result.IsFailed)
            {
                if (result.HasError<MissingBuilding>())
                {
                    logger.Warning("No rally point in this village, cannot dodge.");
                    return Skip.Error.WithErrors(result.Errors);
                }
                return result;
            }

            var sendResult = await sendReinforcementCommand.HandleAsync(new(task.VillageId, troopSlot, target.Value), cancellationToken);
            if (sendResult.IsFailed) return Result.Fail(sendResult.Errors);

            // Bring the troops back home ~5 minutes after the ATTACK lands (not after our own
            // troops arrive at the safe village). Falls back to our own arrival time, then a
            // fixed default, if the attack's arrival time couldn't be read.
            var recallAt = attackArrivalTime ?? sendResult.Value ?? DateTime.Now.AddMinutes(20);
            var recallTask = new RecallTroopTask.Task(task.AccountId, task.VillageId)
            {
                ExecuteAt = recallAt.AddMinutes(5),
            };
            taskManager.Add(recallTask);

            return Result.Ok();
        }
    }
}
