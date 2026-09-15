using MainCore.Commands.Features.HeroRevive;
using MainCore.Commands.Features.NpcResource;
using MainCore.Commands.Features.StartAdventure;
using MainCore.Commands.Features.UseHeroItem;
using MainCore.Tasks.Base;

namespace MainCore.Tasks
{
    // User-requested 3-tier hero revival fallback (2026-09-12): (1) hero's own bag resources,
    // rounded up to the nearest 100 (excess accepted - user's explicit request; not a real
    // game constraint, see CLAUDE.md 2j, but the user wants the buffer anyway); (2) pull spare
    // resources from sibling villages via SupplyForReviveTask, mirroring AutoSettle's
    // demand-driven NeedExpansion*/SupplyForSettleTask pattern; (3) NPC trade (costs gold -
    // user confirmed this is acceptable) once siblings have had a few cycles' fair chance to
    // actually deliver. AccountTask, not VillageTask - the hero avatar/Attributes page is
    // reachable from any village, and revival always targets the hero's actual home village
    // (HeroParser.GetHomeVillageGameId) regardless of which village happens to be active when
    // this runs, so there's no single "right" VillageId to tie this task to.
    //
    // IMPORTANT: per TimerManager.Execute, an AccountTask that finishes with its ExecuteAt
    // unchanged from when it started gets REMOVED from the queue entirely (treated as a
    // one-shot task) - only a bumped ExecuteAt keeps it alive for the next cycle (see
    // StartAdventureTask/NextExecuteStartAdventureTaskCommand for the same pattern). Every
    // return path below that means "checked, try again later" explicitly bumps
    // task.ExecuteAt by RecheckInterval before returning - this is NOT optional here the way
    // it might look from a plain Result.Ok(), and is why parse-failure paths use Skip (bumped)
    // rather than Retry: Retry gets three in-process Polly retries (TimerManager's pipeline)
    // and then PAUSES THE WHOLE ACCOUNT if still failing - too disruptive for "couldn't parse
    // the hero page this one time", which should just be quietly retried next cycle instead.
    //
    // CONFIRMED (2026-09-12, user verified against their live account): the revive resource
    // icons' transfer always targets the hero's home village specifically, regardless of
    // which village happens to be "currently active" in the browser when clicked. Tier 1's
    // bag-use step correctly relies on this - it never needs to navigate to/select the home
    // village first, it just clicks the revive icon from wherever the hero page was reached.
    [Handler]
    public static partial class HeroReviveTask
    {
        public sealed class Task : AccountTask
        {
            public Task(AccountId accountId) : base(accountId)
            {
            }

            protected override string TaskName => "Revive hero";

            public override bool CanStart(AppDbContext context)
            {
                return context.BooleanByName(AccountId, AccountSettingEnums.EnableAutoHeroRevive);
            }
        }

        // How often to re-check hero status when nothing needs to happen right now (hero
        // alive, or a gap remains but we're still waiting on siblings/between NPC attempts).
        // Hero death is rare and revival itself takes hours even in the best case, so this
        // doesn't need to be tight.
        private static readonly TimeSpan RecheckInterval = TimeSpan.FromMinutes(10);

        // How many consecutive cycles to let sibling-village requests (tier 2) sit before
        // escalating to NPC trade (tier 3) - gives merchants a real chance to actually arrive
        // (travel time is asynchronous, see SupplyForReviveTask) rather than spending gold
        // every single cycle a gap still remains.
        private const int MinCyclesBeforeNpcTrade = 3;

        private static long RoundUpTo100(long res)
        {
            if (res <= 0) return 0;
            var remainder = res % 100;
            return remainder == 0 ? res : res + (100 - remainder);
        }

        private static long[] GetDeficit(Dictionary<string, long> targetAmounts, Storage storage) =>
        [
            Math.Max(0, targetAmounts["lumber"] - storage.Wood),
            Math.Max(0, targetAmounts["clay"] - storage.Clay),
            Math.Max(0, targetAmounts["iron"] - storage.Iron),
            Math.Max(0, targetAmounts["crop"] - storage.Crop),
        ];

        private static void SetSetting(AppDbContext context, VillageId villageId, VillageSettingEnums setting, long value)
        {
            var clamped = (int)Math.Clamp(value, 0, int.MaxValue);
            context.VillagesSetting
                .Where(x => x.VillageId == villageId.Value && x.Setting == setting)
                .ExecuteUpdate(x => x.SetProperty(x => x.Value, clamped));
        }

        private static void UpdateNeedRevive(AppDbContext context, AccountId accountId, VillageId villageId, ITaskManager taskManager, ILogger logger, long[] deficit)
        {
            SetSetting(context, villageId, VillageSettingEnums.NeedReviveWood, deficit[0]);
            SetSetting(context, villageId, VillageSettingEnums.NeedReviveClay, deficit[1]);
            SetSetting(context, villageId, VillageSettingEnums.NeedReviveIron, deficit[2]);
            SetSetting(context, villageId, VillageSettingEnums.NeedReviveCrop, deficit[3]);

            if (deficit.Any(x => x > 0))
            {
                SupplyForReviveTask.RequestIfNeeded(context, accountId, villageId, taskManager, logger);
            }
        }

        private static async ValueTask<Result> HandleAsync(
            Task task,
            AppDbContext context,
            IChromeBrowser browser,
            CheckHeroHealthCommand.Handler checkHeroHealthCommand,
            ValidateEnoughResourceCommand.Handler validateEnoughHeroResourceCommand,
            UseHeroReviveResourceCommand.Handler useHeroReviveResourceCommand,
            ToNpcResourcePageCommand.Handler toNpcResourcePageCommand,
            NpcExchangeToTargetCommand.Handler npcExchangeToTargetCommand,
            ITaskManager taskManager,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            // CheckHeroHealthCommand already navigates to /hero/attributes and waits for the
            // page to load - reused here purely for that navigation. Its own health-percent
            // return value isn't otherwise needed by this task; confirmed (2026-09-12, from the
            // live dead-hero HTML sample) that HeroParser.GetHealthPercent reads 0 rather than
            // failing on a dead hero, so this navigation succeeds either way.
            var healthResult = await checkHeroHealthCommand.HandleAsync(new(), cancellationToken);
            if (healthResult.IsFailed)
            {
                task.ExecuteAt = DateTime.Now.Add(RecheckInterval);
                return Result.Fail(healthResult.Errors);
            }

            if (!HeroParser.IsHeroDead(browser.Html))
            {
                // Hero is alive - nothing to do this cycle.
                task.ExecuteAt = DateTime.Now.Add(RecheckInterval);
                return Result.Ok();
            }

            var targetAmounts = HeroParser.GetReviveTargetAmounts(browser.Html);
            if (targetAmounts is null)
            {
                logger.Warning("Hero is dead but couldn't read the revive target resource amounts from the page.");
                task.ExecuteAt = DateTime.Now.Add(RecheckInterval);
                return Skip.Error;
            }

            var homeVillageGameId = HeroParser.GetHomeVillageGameId(browser.Html);
            if (homeVillageGameId is null)
            {
                logger.Warning("Hero is dead but couldn't read its home village from the page.");
                task.ExecuteAt = DateTime.Now.Add(RecheckInterval);
                return Skip.Error;
            }

            var homeVillage = context.Villages.FirstOrDefault(x => x.Id == homeVillageGameId.Value && x.AccountId == task.AccountId.Value);
            if (homeVillage is null)
            {
                logger.Warning("Hero's home village (game id {GameId}) isn't a known village on this account - can't determine where to send revival resources.", homeVillageGameId);
                task.ExecuteAt = DateTime.Now.Add(RecheckInterval);
                return Skip.Error;
            }
            var homeVillageId = new VillageId(homeVillage.Id);

            var storage = context.Storages.FirstOrDefault(x => x.VillageId == homeVillageId.Value);
            if (storage is null)
            {
                task.ExecuteAt = DateTime.Now.Add(RecheckInterval);
                return Skip.Error;
            }

            var deficit = GetDeficit(targetAmounts, storage);

            if (deficit.All(x => x == 0))
            {
                logger.Information("Hero revive resources already sufficient in {VillageId}, waiting for the game's own auto-revive.", homeVillageId);
                SetSetting(context, homeVillageId, VillageSettingEnums.ReviveWaitingCycles, 0);
                task.ExecuteAt = DateTime.Now.Add(RecheckInterval);
                return Result.Ok();
            }

            // Tier 1: hero's own bag, rounded up to the nearest 100 (see class comment). No
            // village-storage-capacity clamp needed here the way UseHeroResourceCommand needs
            // one for building top-ups: the revive target amounts THE GAME ITSELF gave us are
            // already what fits (it computed them), so there's no separate capacity ceiling to
            // respect on top of that.
            var roundedDeficit = deficit.Select(RoundUpTo100).ToArray();
            var validateResult = await validateEnoughHeroResourceCommand.HandleAsync(new(task.AccountId, roundedDeficit), cancellationToken);
            if (validateResult.IsFailed)
            {
                logger.Information("Hero's bag doesn't have enough to fully cover the revive top-up right now - skipping bag use this cycle, trying siblings/NPC instead.");
            }
            else
            {
                var bagItemToUse = new Dictionary<HeroItemEnums, long>
                {
                    { HeroItemEnums.Wood, roundedDeficit[0] },
                    { HeroItemEnums.Clay, roundedDeficit[1] },
                    { HeroItemEnums.Iron, roundedDeficit[2] },
                    { HeroItemEnums.Crop, roundedDeficit[3] },
                };

                var useResult = await useHeroReviveResourceCommand.HandleAsync(new(bagItemToUse), cancellationToken);
                if (useResult.IsFailed)
                {
                    task.ExecuteAt = DateTime.Now.Add(RecheckInterval);
                    return useResult;
                }

                var updatedStorage = context.Storages.FirstOrDefault(x => x.VillageId == homeVillageId.Value);
                if (updatedStorage is not null) deficit = GetDeficit(targetAmounts, updatedStorage);
            }

            if (deficit.All(x => x == 0))
            {
                logger.Information("Hero fully topped up using its own bag resources in {VillageId}.", homeVillageId);
                UpdateNeedRevive(context, task.AccountId, homeVillageId, taskManager, logger, [0, 0, 0, 0]);
                SetSetting(context, homeVillageId, VillageSettingEnums.ReviveWaitingCycles, 0);
                task.ExecuteAt = DateTime.Now.Add(RecheckInterval);
                return Result.Ok();
            }

            // Tier 2: pull spare resources from sibling villages (mirrors AutoSettle's
            // NeedExpansion*/SupplyForSettleTask pattern exactly, but for revival - see
            // SupplyForReviveTask). This is NOT instant: merchants take real travel time, so
            // this only requests delivery; MinCyclesBeforeNpcTrade below gives them a fair
            // chance to actually arrive before this task starts spending gold on tier 3.
            UpdateNeedRevive(context, task.AccountId, homeVillageId, taskManager, logger, deficit);

            var waitingCycles = context.ByName(homeVillageId, VillageSettingEnums.ReviveWaitingCycles);
            if (waitingCycles < MinCyclesBeforeNpcTrade)
            {
                SetSetting(context, homeVillageId, VillageSettingEnums.ReviveWaitingCycles, waitingCycles + 1);
                logger.Information(
                    "Hero still short {Deficit} in {VillageId} after bag use - requested sibling villages, waiting ({Cycles}/{Min} cycles) before trying NPC trade.",
                    deficit, homeVillageId, waitingCycles + 1, MinCyclesBeforeNpcTrade);
                task.ExecuteAt = DateTime.Now.Add(RecheckInterval);
                return Result.Ok();
            }

            // Tier 3: NPC trade (costs gold - user confirmed acceptable 2026-09-12). Reached
            // once siblings have had their fair chance and a gap still remains - still helps
            // even with sibling deliveries still in flight, by better-utilizing whatever the
            // home village already has on hand in the meantime.
            var pageResult = await toNpcResourcePageCommand.HandleAsync(new(homeVillageId), cancellationToken);
            if (pageResult.IsFailed)
            {
                task.ExecuteAt = DateTime.Now.Add(RecheckInterval);
                if (pageResult.HasError<MissingBuilding>())
                {
                    logger.Warning("Home village {VillageId} has no marketplace - can't NPC-trade toward hero revival.", homeVillageId);
                    return Skip.Error.WithErrors(pageResult.Errors);
                }
                return Stop.Error.WithErrors(pageResult.Errors);
            }

            var npcTarget = new long[]
            {
                targetAmounts["lumber"],
                targetAmounts["clay"],
                targetAmounts["iron"],
                targetAmounts["crop"],
            };
            var npcResult = await npcExchangeToTargetCommand.HandleAsync(new(homeVillageId, npcTarget), cancellationToken);
            if (npcResult.IsFailed)
            {
                task.ExecuteAt = DateTime.Now.Add(RecheckInterval);
                return npcResult;
            }

            logger.Information("Used NPC trade toward hero revival in {VillageId}.", homeVillageId);
            SetSetting(context, homeVillageId, VillageSettingEnums.ReviveWaitingCycles, 0);
            task.ExecuteAt = DateTime.Now.Add(RecheckInterval);

            return Result.Ok();
        }
    }
}
