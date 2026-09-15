namespace MainCore.Commands.Features.NpcResource
{
    // Tier 3 (last resort) of hero revival's 3-tier fallback (2026-09-12, see HeroReviveTask) -
    // costs gold (NpcResourceParser.GetExchangeResourcesButton requires the "gold" class, same
    // as NpcResourceCommand - user confirmed this is acceptable). Unlike NpcResourceCommand
    // (which redistributes the village's current total resource sum according to configured
    // AutoNPC*/ratio settings), this sets EXACT absolute target amounts computed from the
    // hero's actual revival shortfall. NPC trade can only REDISTRIBUTE the village's current
    // total, not create resources - if the sum of all four targets exceeds what the village
    // currently holds in total, this gets as close as it can (each resource's proportional
    // share of the target) rather than failing outright; HeroReviveTask re-checks the
    // remaining gap afterward.
    [Handler]
    public static partial class NpcExchangeToTargetCommand
    {
        // Target is Wood/Clay/Iron/Crop order throughout, matching GetMissingResourceCommand/
        // UseHeroResourceCommand's convention - absolute desired amounts, not deltas.
        public sealed record Command(VillageId VillageId, long[] Target) : IVillageCommand;

        private static async ValueTask<Result> HandleAsync(
            Command command,
            IChromeBrowser browser,
            AppDbContext context,
            CancellationToken cancellationToken)
        {
            var (villageId, target) = command;

            var result = await OpenNPCDialog(browser, cancellationToken);
            if (result.IsFailed) return result;

            var sum = NpcResourceParser.GetSum(browser.Html);
            if (sum < 0) return Retry.Error.WithError("Failed to read NPC dialog resource sum");

            var desired = GetDesiredValues(target, sum);

            var storage = context.Storages.FirstOrDefault(x => x.VillageId == villageId.Value);
            if (storage is not null)
            {
                desired = ClampToStorageCapacity(desired, storage);
            }

            result = await InputAmount(browser, desired, cancellationToken);
            if (result.IsFailed) return result;

            result = await Redeem(browser, cancellationToken);
            if (result.IsFailed) return result;

            result = await browser.Wait(driver =>
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(driver.PageSource);
                return !NpcResourceParser.IsNpcDialog(doc);
            }, cancellationToken);
            if (result.IsFailed) return result;

            return Result.Ok();
        }

        // If the total of all four targets fits within what the village currently holds
        // (summed across all resource types), give each resource exactly its target and dump
        // the untargeted leftover into crop (matches NpcResourceCommand.GetValues' own
        // leftover-handling convention). Otherwise the total need exceeds what redistribution
        // alone can provide - give each resource its proportional share of the target instead,
        // so the mix moves toward what's needed even though the total gap can't close here.
        private static long[] GetDesiredValues(long[] target, long sum)
        {
            var sumTarget = target.Sum();
            var desired = new long[4];

            if (sumTarget <= sum)
            {
                for (var i = 0; i < 4; i++) desired[i] = target[i];
                desired[3] += sum - sumTarget;
            }
            else if (sumTarget > 0)
            {
                for (var i = 0; i < 4; i++) desired[i] = sum * target[i] / sumTarget;
                var diff = sum - desired.Sum();
                desired[3] += diff;
            }
            else
            {
                desired[3] = sum;
            }

            return desired;
        }

        // Same capacity semantics as UseHeroResourceCommand.ClampToStorageCapacity
        // (Storage.Warehouse/Granary hold CAPACITY, not current fill - see CLAUDE.md 2j).
        // NPC trade sets the ABSOLUTE post-trade value (not an addition on top of current
        // stock), so the cap here is just the capacity itself. Any amount trimmed off a
        // resource that's over its capacity gets pushed to whichever other resource still has
        // room - keeping the redistributed sum intact matters more here than hitting the exact
        // target - falling back to leaving it on the last resource if nothing else has room
        // (the game will reject/clamp that on submit, same as any other over-capacity input).
        private static long[] ClampToStorageCapacity(long[] desired, Storage storage)
        {
            var capacity = new[] { storage.Warehouse, storage.Warehouse, storage.Warehouse, storage.Granary };

            var clamped = (long[])desired.Clone();
            var totalOverflow = 0L;
            for (var i = 0; i < 4; i++)
            {
                if (clamped[i] > capacity[i])
                {
                    totalOverflow += clamped[i] - capacity[i];
                    clamped[i] = capacity[i];
                }
            }

            var pass = 0;
            while (totalOverflow > 0 && pass < 4)
            {
                pass++;
                var room = Enumerable.Range(0, 4).Where(i => clamped[i] < capacity[i]).ToList();
                if (room.Count == 0) break;
                var share = totalOverflow / room.Count;
                if (share <= 0) share = totalOverflow;
                foreach (var i in room)
                {
                    var add = Math.Min(share, capacity[i] - clamped[i]);
                    clamped[i] += add;
                    totalOverflow -= add;
                    if (totalOverflow <= 0) break;
                }
            }

            if (totalOverflow > 0) clamped[3] += totalOverflow;

            return clamped;
        }

        private static async Task<Result> OpenNPCDialog(IChromeBrowser browser, CancellationToken cancellationToken)
        {
            var (_, isFailed, element, errors) = await browser.GetElement(doc => NpcResourceParser.GetExchangeResourcesButton(doc), cancellationToken);
            if (isFailed) return Result.Fail(errors);

            var result = await browser.Click(element, cancellationToken);
            if (result.IsFailed) return result;

            static bool DialogShown(IWebDriver driver)
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(driver.PageSource);
                return NpcResourceParser.IsNpcDialog(doc);
            }

            result = await browser.Wait(DialogShown, cancellationToken);
            if (result.IsFailed) return result;

            return Result.Ok();
        }

        private static async Task<Result> InputAmount(IChromeBrowser browser, long[] values, CancellationToken cancellationToken)
        {
            var inputs = NpcResourceParser.GetInputs(browser.Html).ToArray();

            for (var i = 0; i < 4; i++)
            {
                var (_, isFailed, element, errors) = await browser.GetElement(By.XPath(inputs[i].XPath), cancellationToken);
                if (isFailed) return Result.Fail(errors);

                var result = await browser.Input(element, $"{values[i]}", cancellationToken);
                if (result.IsFailed) return result;
            }

            return Result.Ok();
        }

        private static async Task<Result> Redeem(IChromeBrowser browser, CancellationToken cancellationToken)
        {
            var (_, isFailed, element, errors) = await browser.GetElement(doc => NpcResourceParser.GetRedeemButton(doc), cancellationToken);
            if (isFailed) return Result.Fail(errors);

            var result = await browser.Click(element, cancellationToken);
            if (result.IsFailed) return result;

            return Result.Ok();
        }
    }
}
