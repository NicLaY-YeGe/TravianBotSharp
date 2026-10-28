using MainCore.Commands.Features.DodgeTroop;
using MainCore.Commands.Features.OasisScout;
using MainCore.Commands.Features.StartAdventure;
using MainCore.Commands.Features.SyncAttack;
using MainCore.Commands.NextExecute;
using MainCore.Tasks.Base;

namespace MainCore.Tasks
{
    // 2026-09-15, user request ("hero canlandırma" conversation's follow-up): on each run,
    // reads this village's /report/surrounding oasis-plunder reports, dedupes to unique
    // coordinates within OasisScoutMaxDistance, and works outward from the nearest one:
    //   - animals present: estimate their total attack power (AnimalPower, a deliberately
    //     rough table - see its own comment) against the hero's real current attack power
    //     (GetHeroAttackPowerCommand); send the hero ALONE only if that estimate is under
    //     OasisScoutHeroPowerThreshold% of the hero's power, otherwise skip this oasis.
    //   - empty: send a random(OasisScoutMinTroops, OasisScoutMaxTroops) amount of the
    //     configured OasisScoutTroopSlot unit.
    //
    // No per-oasis cooldown (explicit user choice, 2026-09-15) - every run re-checks every
    // candidate's LIVE state fresh, since animal populations regenerate over time anyway.
    // Failures for one candidate oasis (tile wouldn't open, not enough troops right now, hero
    // already busy elsewhere, server rejected the send) are logged and skipped - they do NOT
    // fail the whole task/pause the bot, since running out of troops or a single bad tile is
    // routine for an unattended scan (same philosophy as RaidListTask's insufficient-troops
    // handling). Only a failure to load /report/surrounding itself propagates upward, since
    // that's a real navigation/parsing problem worth pausing on.
    [Handler]
    public static partial class OasisScoutTask
    {
        public sealed class Task : VillageTask
        {
            public Task(AccountId accountId, VillageId villageId) : base(accountId, villageId)
            {
            }

            protected override string TaskName => "Oasis scout";

            public override bool CanStart(AppDbContext context)
            {
                return context.BooleanByName(VillageId, VillageSettingEnums.EnableOasisScout);
            }
        }

        private static async ValueTask<Result> HandleAsync(
            Task task,
            ISettingService settingService,
            IChromeBrowser browser,
            ToSurroundingReportPageCommand.Handler toSurroundingReportPageCommand,
            ToMapTileCommand.Handler toMapTileCommand,
            CloseTileDialogCommand.Handler closeTileDialogCommand,
            ToSendTroopsPageCommand.Handler toSendTroopsPageCommand,
            SendTroopsCommand.Handler sendTroopsCommand,
            GetHeroAttackPowerCommand.Handler getHeroAttackPowerCommand,
            CheckHeroHealthCommand.Handler checkHeroHealthCommand,
            NextExecuteOasisScoutTaskCommand.Handler nextExecuteOasisScoutTaskCommand,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            var villageId = task.VillageId;

            // 2026-10-25, user decision: with the hero below the same minimum health the adventure uses
            // (MinHeroHealthPercent, 0 = no restriction) Oasis scout does not run at all - no page
            // visits, no sends - and looks again later. In the 2026-10-24 log the hero stood at 0% and
            // every run spent 5-6 oasis tiles on sends that could only fail. The reading is shared
            // with StartAdventureTask (HeroHealthReader), so this costs at most one hero page load
            // per ten minutes for the whole account. A health page that cannot be read is treated as
            // "not safe to send" for this run (never as healthy); defensive/secondary rule: it never
            // fails the task (CLAUDE.md rule 4), only a closed browser or a user pause pass through.
            var minHeroHealthPercent = settingService.ByName(task.AccountId, AccountSettingEnums.MinHeroHealthPercent);
            if (minHeroHealthPercent > 0)
            {
                var healthResult = await HeroHealthReader.GetAsync(task.AccountId, checkHeroHealthCommand, cancellationToken);
                if (healthResult.IsFailed)
                {
                    if (healthResult.HasError<BrowserClosed>() || healthResult.HasError<Cancel>())
                    {
                        return Result.Fail(healthResult.Errors);
                    }

                    task.ExecuteAt = DateTime.Now.AddMinutes(HeroHealthGate.FailedReadRecheckMinutes);
                    logger.Warning(
                        "Oasis scout: could not read the hero's health ({Error}) - not sending anything now, looking again at {Time:HH:mm}.",
                        healthResult.Errors.FirstOrDefault()?.Message, task.ExecuteAt);
                    return Result.Ok();
                }

                if (HeroHealthGate.IsBelowMinimum(healthResult.Value, minHeroHealthPercent))
                {
                    task.ExecuteAt = DateTime.Now.AddMinutes(HeroHealthGate.RecheckMinutes);
                    logger.Information(
                        "Oasis scout: hero health {HealthPercent}% is below the configured minimum {MinPercent}% - skipping this run, looking again at {Time:HH:mm}.",
                        healthResult.Value, minHeroHealthPercent, task.ExecuteAt);
                    return Result.Ok();
                }
            }

            var maxDistance = settingService.ByName(villageId, VillageSettingEnums.OasisScoutMaxDistance);
            var troopSlot = settingService.ByName(villageId, VillageSettingEnums.OasisScoutTroopSlot);
            var minTroops = settingService.ByName(villageId, VillageSettingEnums.OasisScoutMinTroops);
            var maxTroops = settingService.ByName(villageId, VillageSettingEnums.OasisScoutMaxTroops);
            var powerThresholdPercent = settingService.ByName(villageId, VillageSettingEnums.OasisScoutHeroPowerThreshold);

            var reportResult = await toSurroundingReportPageCommand.HandleAsync(new(), cancellationToken);
            if (reportResult.IsFailed) return reportResult;

            var coordinates = SurroundingReportParser.GetUniqueOasisCoordinates(browser.Html, maxDistance);
            if (coordinates.Count == 0)
            {
                logger.Information("Oasis scout: no oasis reports within {MaxDistance} fields of village {VillageId}.", maxDistance, villageId);
                await nextExecuteOasisScoutTaskCommand.HandleAsync(new(task), cancellationToken);
                return Result.Ok();
            }

            // Fetched at most once per run (not once per candidate) - it can't change
            // mid-run, and each fetch is a full page navigation, so this saves a round trip
            // per extra oasis checked. Left null if animals never actually need checking
            // (e.g. every candidate this run turns out empty).
            int? heroPower = null;

            foreach (var (x, y, distance) in coordinates)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var tileResult = await toMapTileCommand.HandleAsync(new(x, y), cancellationToken);
                if (tileResult.IsFailed)
                {
                    logger.Warning("Oasis scout: could not open tile ({X}|{Y}) - {Error}", x, y, tileResult.Errors.FirstOrDefault()?.Message);
                    continue;
                }

                if (!OasisTileParser.IsUnoccupiedOasis(browser.Html))
                {
                    logger.Information("Oasis scout: ({X}|{Y}) is no longer an unoccupied oasis - skipping.", x, y);
                    await closeTileDialogCommand.HandleAsync(new(), cancellationToken);
                    continue;
                }

                var animals = OasisTileParser.GetAnimals(browser.Html);

                // 2026-10-24: everything needed from the tile dialog is read; close it so it cannot cover
                // the navigation bar for the next step (see CloseTileDialogCommand).
                await closeTileDialogCommand.HandleAsync(new(), cancellationToken);

                if (animals.Count == 0)
                {
                    if (troopSlot <= 0 || maxTroops <= 0)
                    {
                        logger.Information("Oasis scout: ({X}|{Y}) is empty but no troop slot/amount is configured - skipping.", x, y);
                        continue;
                    }

                    var amount = minTroops >= maxTroops ? Math.Max(0, minTroops) : Random.Shared.Next(Math.Max(0, minTroops), maxTroops + 1);
                    if (amount <= 0) continue;

                    var sendResult = await SendToOasis(
                        villageId, x, y, new Dictionary<int, long> { [troopSlot] = amount }, false,
                        toSendTroopsPageCommand, sendTroopsCommand, browser, cancellationToken);

                    if (sendResult.IsFailed)
                    {
                        logger.Warning(
                            "Oasis scout: sending {Amount} troops (slot {Slot}) to empty oasis ({X}|{Y}) failed - {Error}",
                            amount, troopSlot, x, y, sendResult.Errors.FirstOrDefault()?.Message);
                        continue;
                    }

                    logger.Information(
                        "Oasis scout: sent {Amount} troops (slot {Slot}) to empty oasis ({X}|{Y}), {Distance} fields away.",
                        amount, troopSlot, x, y, distance);
                }
                else
                {
                    if (heroPower is null)
                    {
                        var powerResult = await getHeroAttackPowerCommand.HandleAsync(new(), cancellationToken);
                        if (powerResult.IsFailed)
                        {
                            logger.Warning(
                                "Oasis scout: could not read hero attack power - skipping remaining animal oases this run.");
                            break;
                        }
                        heroPower = powerResult.Value;
                    }

                    var animalPower = AnimalPower.TotalPower(animals);
                    var threshold = heroPower.Value * powerThresholdPercent / 100.0;

                    if (animalPower > threshold)
                    {
                        logger.Information(
                            "Oasis scout: ({X}|{Y}) estimated animal power {AnimalPower} exceeds threshold {Threshold:F0} (hero power {HeroPower}, {Percent}%) - too risky, skipping hero.",
                            x, y, animalPower, threshold, heroPower, powerThresholdPercent);
                        continue;
                    }

                    var sendResult = await SendToOasis(
                        villageId, x, y, new Dictionary<int, long>(), true,
                        toSendTroopsPageCommand, sendTroopsCommand, browser, cancellationToken);

                    if (sendResult.IsFailed)
                    {
                        logger.Warning("Oasis scout: sending hero to ({X}|{Y}) failed - {Error}", x, y, sendResult.Errors.FirstOrDefault()?.Message);
                        continue;
                    }

                    logger.Information(
                        "Oasis scout: sent hero alone to ({X}|{Y}) - estimated animal power {AnimalPower} within threshold {Threshold:F0}.",
                        x, y, animalPower, threshold);
                }
            }

            await nextExecuteOasisScoutTaskCommand.HandleAsync(new(task), cancellationToken);
            return Result.Ok();
        }

        // Own time limit for opening the Send troops page (ToDorf + building + tab switch normally ~3 s).
        private const int NavigationTimeoutSeconds = 45;

        // Pre-checks troop/hero availability against the currently loaded Send Troops page
        // BEFORE calling SendTroopsCommand, same as RaidListTask does - SendTroopsCommand's
        // own internal availability check returns a generic Retry-class error (shared with
        // every other caller, where running short really should pause the bot), which here
        // would be indistinguishable from a real problem. Returning Skip.Error here instead
        // lets the caller's own log-and-continue loop treat "not enough troops/hero busy
        // right now" as routine.
        private static async Task<Result> SendToOasis(
            VillageId villageId, int x, int y,
            Dictionary<int, long> troopAmounts, bool includeHero,
            ToSendTroopsPageCommand.Handler toSendTroopsPageCommand,
            SendTroopsCommand.Handler sendTroopsCommand,
            IChromeBrowser browser,
            CancellationToken cancellationToken)
        {
            // 2026-10-24: the navigation used to wait up to 180 s per element (about 20 stalls of 3-9 minutes in
            // one 8 h log). A send is routine work, so it gets its own limit and is skipped when it is exceeded.
            using var navigationCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            navigationCts.CancelAfter(TimeSpan.FromSeconds(NavigationTimeoutSeconds));
            Result toPageResult;
            try
            {
                toPageResult = await toSendTroopsPageCommand.HandleAsync(new(villageId), navigationCts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return Skip.Error.WithError($"village {villageId}: opening the Send troops page took longer than {NavigationTimeoutSeconds}s");
            }

            if (toPageResult.IsFailed)
            {
                if (navigationCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                {
                    return Skip.Error.WithError($"village {villageId}: opening the Send troops page took longer than {NavigationTimeoutSeconds}s");
                }
                return toPageResult;
            }

            foreach (var (slot, amount) in troopAmounts)
            {
                if (amount <= 0) continue;

                var available = RallyPointSendTroopsParser.GetAvailableTroopCount(browser.Html, slot);
                if (available < amount)
                {
                    return Skip.Error.WithError($"village {villageId}: not enough troops in slot {slot} (needs {amount}, has {available})");
                }
            }

            if (includeHero)
            {
                const int heroSlot = 11;
                var heroAvailable = RallyPointSendTroopsParser.GetAvailableTroopCount(browser.Html, heroSlot);
                if (heroAvailable < 1)
                {
                    return Skip.Error.WithError($"village {villageId}: hero not available (already on an adventure/other movement?)");
                }
            }

            var sendResult = await sendTroopsCommand.HandleAsync(
                new(villageId, x, y, RallyPointEventTypeEnums.AttackRaid, troopAmounts, Confirm: true, IncludeHero: includeHero, TimeoutSeconds: 60),
                cancellationToken);
            if (sendResult.IsFailed) return Result.Fail(sendResult.Errors);

            return Result.Ok();
        }
    }
}
