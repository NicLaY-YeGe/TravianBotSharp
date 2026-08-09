using MainCore.Commands.Features.DodgeTroop;
using MainCore.Commands.Features.SyncAttack;
using MainCore.DTO;
using MainCore.Tasks.Base;

namespace MainCore.Tasks
{
    // Runs once per synchronized-arrival plan the user configures in the Sync Attack tab.
    // For each source village: switches the browser to that village, opens the Rally Point
    // "Send troops" form, and does a PROBE send (SendTroopsCommand, Confirm=false) to learn the
    // real travel time from the game server itself - see SendTroopsCommand for why this is
    // preferred over a hand-rolled speed formula. Once every village's travel time is known,
    // SyncAttackPlanner works out one shared arrival time and a per-village send time, and this
    // task schedules a SendTroopsAtTimeTask (VillageTask) for each village at its computed
    // ExecuteAt - those are what actually submit the real, confirmed sends later.
    //
    // NOTE: because a single account uses a single browser, only one village's send can be
    // physically submitted at a time (see SyncAttackPlanner.SafetyBuffer). If two source
    // villages need to send within a couple of seconds of each other, a few seconds of drift
    // in actual arrival is possible - this is a hard limitation of the one-browser-per-account
    // architecture, not something this feature can fully eliminate.
    [Handler]
    public static partial class SyncAttackPlanTask
    {
        public sealed class Task : AccountTask
        {
            public SyncAttackPlan Plan { get; }

            public Task(AccountId accountId, SyncAttackPlan plan) : base(accountId)
            {
                Plan = plan;
            }

            protected override string TaskName => "Synchronized attack - calculate & schedule";
        }

        private static async ValueTask<Result> HandleAsync(
            Task task,
            SwitchVillageCommand.Handler switchVillageCommand,
            ToSendTroopsPageCommand.Handler toSendTroopsPageCommand,
            SendTroopsCommand.Handler sendTroopsCommand,
            ITaskManager taskManager,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            var plan = task.Plan;
            var travelTimes = new Dictionary<VillageId, TimeSpan>();

            foreach (var order in plan.Villages)
            {
                if (cancellationToken.IsCancellationRequested) return Cancel.Error;

                var switchResult = await switchVillageCommand.HandleAsync(new(order.VillageId), cancellationToken);
                if (switchResult.IsFailed) return switchResult;

                var toPageResult = await toSendTroopsPageCommand.HandleAsync(new(order.VillageId), cancellationToken);
                if (toPageResult.IsFailed)
                {
                    if (toPageResult.HasError<MissingBuilding>())
                    {
                        logger.Warning("Village {VillageId} has no rally point, skipping it in this synchronized attack.", order.VillageId);
                        continue;
                    }
                    return toPageResult;
                }

                var probeStart = DateTime.Now;
                var probeResult = await sendTroopsCommand.HandleAsync(
                    new(order.VillageId, plan.TargetX, plan.TargetY, plan.EventType, order.TroopAmounts, Confirm: false),
                    cancellationToken);
                if (probeResult.IsFailed) return Result.Fail(probeResult.Errors);

                if (probeResult.Value is null)
                {
                    logger.Warning("Could not read a travel time for village {VillageId}, skipping it in this synchronized attack.", order.VillageId);
                    continue;
                }

                var travelTime = probeResult.Value.Value - probeStart;
                if (travelTime < TimeSpan.Zero) travelTime = TimeSpan.Zero;
                travelTimes[order.VillageId] = travelTime;

                logger.Information("Village {VillageId}: travel time to ({X}|{Y}) is {TravelTime}.", order.VillageId, plan.TargetX, plan.TargetY, travelTime);
            }

            if (travelTimes.Count == 0)
            {
                return Skip.Error.WithError("No source village could be probed for this synchronized attack.");
            }

            var planResult = SyncAttackPlanner.ComputeSendTimes(travelTimes, plan.ArrivalMode, plan.DesiredArrivalTime, DateTime.Now);
            if (planResult.IsFailed)
            {
                logger.Warning("Cannot schedule this synchronized attack: {Message}", string.Join(' ', planResult.Errors.Select(e => e.Message)));
                return Result.Fail(planResult.Errors);
            }

            var (arrivalTime, sendTimes) = planResult.Value;

            foreach (var order in plan.Villages)
            {
                if (!sendTimes.TryGetValue(order.VillageId, out var sendAt)) continue;

                var sendTask = new SendTroopsAtTimeTask.Task(task.AccountId, order.VillageId, plan.TargetX, plan.TargetY, plan.EventType, order.TroopAmounts)
                {
                    ExecuteAt = sendAt,
                };
                taskManager.Add(sendTask);

                logger.Information("Village {VillageId} scheduled to send at {SendAt} to arrive at {ArrivalTime}.", order.VillageId, sendAt, arrivalTime);
            }

            return Result.Ok();
        }
    }
}
