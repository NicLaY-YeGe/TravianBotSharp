using MainCore.Commands.Features.DodgeTroop;
using MainCore.Commands.Features.RaidReport;
using MainCore.Commands.Features.SyncAttack;
using MainCore.Tasks.Base;

namespace MainCore.Tasks
{
    // 2026-10-05, user request ("casus raporlarıma göre asker gönder"): queued by RaidReportTask's
    // scout pass right after it saved a FRESH own-scouting report's garrison (see
    // RaidReportTask.QueueScoutAutoAttack). One task per (source village, target coordinate);
    // it is one-shot - ExecuteAt is never touched after the run, so TimerManager removes it
    // (CLAUDE.md 2l: unchanged ExecuteAt after Ok/Skip = task removed).
    //
    // Decision: exactly the Raid List gate (CombatDecisionRules with CombatSafetyMarginPercent) on
    // the garrison the report just revealed, using the typed troop counts from the account
    // settings (ScoutAutoAttackTroop1..10) from the fixed source village. Conservative skips (all
    // logged, none pause the bot): feature switched off / village not configured / tribe not set,
    // target is one of our own villages, garrison contains animals (an oasis - not a player
    // village), garrison older than CombatCheckMaxAgeHours, verdict Skip, not enough troops in
    // the village right now, server rejects the target. Only a genuine browser/parser failure of
    // the send itself is returned as a failure (the usual Retry/Stop bot behaviour).
    [Handler]
    public static partial class ScoutAutoAttackTask
    {
        public sealed class Task : VillageTask
        {
            public int TargetX { get; }
            public int TargetY { get; }

            public Task(AccountId accountId, VillageId villageId, int targetX, int targetY) : base(accountId, villageId)
            {
                TargetX = targetX;
                TargetY = targetY;
            }

            protected override string TaskName => $"Scout auto attack ({TargetX}|{TargetY})";

            // One per source village AND target - the inherited key would collapse every target
            // of the same village into one task.
            public override string Key => $"{AccountId}-{VillageId}-scoutattack-{TargetX}|{TargetY}";
        }

        private static async ValueTask<Result> HandleAsync(
            Task task,
            AppDbContext context,
            ToSendTroopsPageCommand.Handler toSendTroopsPageCommand,
            SendTroopsCommand.Handler sendTroopsCommand,
            IChromeBrowser browser,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            var accountId = task.AccountId;
            var x = task.TargetX;
            var y = task.TargetY;

            if (!context.BooleanByName(accountId, AccountSettingEnums.EnableScoutAutoAttack)) return Skip.Error;

            var configuredVillageId = context.ByName(accountId, AccountSettingEnums.ScoutAutoAttackVillageId);
            if (configuredVillageId <= 0 || configuredVillageId != task.VillageId.Value) return Skip.Error;

            var villages = context.Villages
                .Where(v => v.AccountId == accountId.Value)
                .ToList();

            if (!villages.Any(v => v.Id == task.VillageId.Value)) return Skip.Error;

            if (villages.Any(v => v.X == x && v.Y == y))
            {
                logger.Information("Scout auto attack: ({X}|{Y}) is one of our own villages - skipped.", x, y);
                return Skip.Error;
            }

            var tribe = (TribeEnums)context.ByName(accountId, AccountSettingEnums.Tribe);
            if (tribe == TribeEnums.Any)
            {
                logger.Warning("Scout auto attack: tribe is not set in Account Setting - skipped ({X}|{Y}).", x, y);
                return Skip.Error;
            }

            var slots = RallyPointTroopSlots.GetSlots(tribe);
            var configured = ReadConfiguredTroops(context, accountId, slots.Count);
            if (configured.Count == 0)
            {
                logger.Warning("Scout auto attack: no troop amounts are configured - skipped ({X}|{Y}).", x, y);
                return Skip.Error;
            }

            var garrisonRow = context.ScoutedTargetGarrisons.FirstOrDefault(g =>
                g.AccountId == accountId.Value && g.X == x && g.Y == y);
            if (garrisonRow is null)
            {
                logger.Information("Scout auto attack: no scouted garrison stored for ({X}|{Y}) - skipped.", x, y);
                return Skip.Error;
            }

            var maxAgeHours = context.ByName(accountId, AccountSettingEnums.CombatCheckMaxAgeHours);
            if ((DateTime.Now - garrisonRow.CapturedAt).TotalHours > maxAgeHours)
            {
                logger.Information("Scout auto attack: garrison at ({X}|{Y}) is older than {Hours} h - skipped.", x, y, maxAgeHours);
                return Skip.Error;
            }

            var garrison = garrisonRow.GetTroops();
            if (garrison.Any(t => t.Troop.GetTribe() == TribeEnums.Nature))
            {
                logger.Information("Scout auto attack: ({X}|{Y}) holds animals (oasis) - skipped.", x, y);
                return Skip.Error;
            }

            var ourTroops = configured
                .Select(kv => (Troop: slots[kv.Key - 1], Count: kv.Value))
                .ToList();

            var (infantryPoints, cavalryPoints) = CombatDecisionRules.SplitAttackPoints(ourTroops);
            var ourAttackPoints = infantryPoints + cavalryPoints;
            var theirDefensePoints = CombatDecisionRules.DefensePoints(garrison, infantryPoints, cavalryPoints);

            var marginPercent = context.ByName(accountId, AccountSettingEnums.CombatSafetyMarginPercent);
            var verdict = CombatDecisionRules.Decide(ourAttackPoints, theirDefensePoints, marginPercent);
            if (verdict == CombatDecisionRules.Verdict.Skip)
            {
                logger.Information(
                    "Scout auto attack: ({X}|{Y}) not attacked - estimated attack {Ours} vs estimated defense {Theirs}.",
                    x, y, ourAttackPoints, theirDefensePoints);
                return Skip.Error;
            }

            var toPageResult = await toSendTroopsPageCommand.HandleAsync(new(task.VillageId), cancellationToken);
            if (toPageResult.IsFailed) return toPageResult;

            foreach (var (slot, amount) in configured)
            {
                var available = RallyPointSendTroopsParser.GetAvailableTroopCount(browser.Html, slot);
                if (available < amount)
                {
                    logger.Information(
                        "Scout auto attack: village {VillageId} has only {Available} of the {Amount} troops needed in slot {Slot} - ({X}|{Y}) skipped.",
                        task.VillageId, available, amount, slot, x, y);
                    return Skip.Error;
                }
            }

            var eventType = context.ByName(accountId, AccountSettingEnums.ScoutAutoAttackType) == 1
                ? RallyPointEventTypeEnums.AttackNormal
                : RallyPointEventTypeEnums.AttackRaid;

            var sendResult = await sendTroopsCommand.HandleAsync(
                new(task.VillageId, x, y, eventType, configured, Confirm: true),
                cancellationToken);
            if (sendResult.IsFailed)
            {
                var isEmptyTarget = sendResult.Errors.Any(e =>
                    e.Message.Contains("no village at these coordinates", StringComparison.OrdinalIgnoreCase));
                if (isEmptyTarget)
                {
                    logger.Warning("Scout auto attack: ({X}|{Y}) has no village there - skipped.", x, y);
                    return Skip.Error;
                }

                return Result.Fail(sendResult.Errors);
            }

            logger.Information(
                "Scout auto attack: sent {EventType} from village {VillageId} to ({X}|{Y}) - estimated attack {Ours} vs defense {Theirs}.",
                eventType, task.VillageId, x, y, ourAttackPoints, theirDefensePoints);

            return Result.Ok();
        }

        // Slot (1-based) -> amount, only slots with a positive amount that exist for the tribe.
        private static Dictionary<int, long> ReadConfiguredTroops(AppDbContext context, AccountId accountId, int slotCount)
        {
            var result = new Dictionary<int, long>();
            for (var slot = 1; slot <= Math.Min(10, slotCount); slot++)
            {
                var setting = (AccountSettingEnums)((int)AccountSettingEnums.ScoutAutoAttackTroop1 + slot - 1);
                var amount = context.ByName(accountId, setting);
                if (amount > 0) result[slot] = amount;
            }
            return result;
        }
    }
}
