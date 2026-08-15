using MainCore.Commands.Features.DodgeTroop;
using MainCore.Commands.Features.SyncAttack;
using MainCore.Commands.Features.WaveAttack;
using MainCore.DTO;
using MainCore.Tasks.Base;

namespace MainCore.Tasks
{
    // Runs once per wave-attack plan configured in the Wave Attack tab. Unlike
    // SyncAttackPlanTask (which coordinates several SOURCE VILLAGES sending once each), this
    // coordinates several SEQUENTIAL SENDS from a SINGLE village: one heavy "opening" wave
    // (optionally with the hero) meant to clear defenders and/or start the wall down, then
    // WaveCount identical smaller waves arriving one after another at a fixed gap. Reuses the
    // same probe-then-schedule approach as SyncAttack (SendTroopsCommand Confirm:false to learn
    // the server's real travel time for each composition - a catapult-heavy main wave and a
    // small repeat wave rarely travel at the same speed, so this can't be a hand-rolled
    // formula) and the existing SendTroopsAtTimeTask to actually submit each wave later at its
    // computed ExecuteAt.
    //
    // Being a VillageTask (not AccountTask like SyncAttackPlanTask), VillageTaskBehavior already
    // switches the browser to the right village before HandleAsync runs - no manual
    // SwitchVillageCommand call needed here (see SendTroopsAtTimeTask's comment for the same
    // reasoning).
    [Handler]
    public static partial class WaveAttackPlanTask
    {
        public sealed class Task : VillageTask
        {
            public WaveAttackPlan Plan { get; }

            public Task(AccountId accountId, VillageId villageId, WaveAttackPlan plan) : base(accountId, villageId)
            {
                Plan = plan;
            }

            protected override string TaskName => "Wave attack - calculate & schedule";
        }

        private static async ValueTask<Result> HandleAsync(
            Task task,
            ToSendTroopsPageCommand.Handler toSendTroopsPageCommand,
            SendTroopsCommand.Handler sendTroopsCommand,
            ITaskManager taskManager,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            var plan = task.Plan;

            var toPageResult = await toSendTroopsPageCommand.HandleAsync(new(task.VillageId), cancellationToken);
            if (toPageResult.IsFailed) return toPageResult;

            var mainProbeStart = DateTime.Now;
            var mainProbe = await sendTroopsCommand.HandleAsync(
                new(task.VillageId, plan.TargetX, plan.TargetY, plan.EventType, plan.MainWaveTroopAmounts, Confirm: false, IncludeHero: plan.MainWaveIncludeHero),
                cancellationToken);
            if (mainProbe.IsFailed) return Result.Fail(mainProbe.Errors);
            if (mainProbe.Value is null)
            {
                return Skip.Error.WithError("Could not read a travel time for the main wave - check troop amounts and target coordinates.");
            }

            var mainTravelTime = mainProbe.Value.Value - mainProbeStart;
            if (mainTravelTime < TimeSpan.Zero) mainTravelTime = TimeSpan.Zero;

            TimeSpan? repeatTravelTime = null;
            if (plan.WaveCount > 0)
            {
                // The main-wave probe above (Confirm:false) navigates back to the Rally Point
                // overview when it's done - the send-troops form has to be reopened before this
                // second probe, or InputTroopAmount would be looking for slot inputs on the
                // wrong page.
                var backToSendPageResult = await toSendTroopsPageCommand.HandleAsync(new(task.VillageId), cancellationToken);
                if (backToSendPageResult.IsFailed) return backToSendPageResult;

                var repeatProbeStart = DateTime.Now;
                var repeatProbe = await sendTroopsCommand.HandleAsync(
                    new(task.VillageId, plan.TargetX, plan.TargetY, plan.EventType, plan.RepeatWaveTroopAmounts, Confirm: false),
                    cancellationToken);
                if (repeatProbe.IsFailed) return Result.Fail(repeatProbe.Errors);
                if (repeatProbe.Value is null)
                {
                    return Skip.Error.WithError("Could not read a travel time for the repeat wave - check troop amounts and target coordinates.");
                }

                repeatTravelTime = repeatProbe.Value.Value - repeatProbeStart;
                if (repeatTravelTime < TimeSpan.Zero) repeatTravelTime = TimeSpan.Zero;
            }

            var planResult = WaveAttackPlanner.ComputeSchedule(mainTravelTime, repeatTravelTime, plan.WaveCount, plan.GapSeconds, DateTime.Now);
            if (planResult.IsFailed)
            {
                logger.Warning("Cannot schedule this wave attack for {VillageId}: {Message}",
                    task.VillageId, string.Join(' ', planResult.Errors.Select(e => e.Message)));
                return Result.Fail(planResult.Errors);
            }

            foreach (var warning in planResult.Value.Warnings)
            {
                logger.Warning("{VillageId}: {Warning}", task.VillageId, warning);
            }

            foreach (var wave in planResult.Value.Waves)
            {
                var amounts = wave.IsMainWave ? plan.MainWaveTroopAmounts : plan.RepeatWaveTroopAmounts;
                var includeHero = wave.IsMainWave && plan.MainWaveIncludeHero;

                var sendTask = new SendTroopsAtTimeTask.Task(task.AccountId, task.VillageId, plan.TargetX, plan.TargetY, plan.EventType, amounts, includeHero)
                {
                    ExecuteAt = wave.SendAt,
                };
                taskManager.Add(sendTask);

                logger.Information(
                    "Village {VillageId}: wave {WaveIndex} ({Kind}) scheduled to send at {SendAt}, expected arrival {ArrivalTime}.",
                    task.VillageId, wave.WaveIndex, wave.IsMainWave ? "main" : "repeat", wave.SendAt, wave.ArrivalTime);
            }

            return Result.Ok();
        }
    }
}
