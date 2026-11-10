namespace MainCore.Commands.Features.BuyCrop
{
    // 2026-11-10: accepts crop offers on the Marketplace "Buy" tab (the page must already show it, see
    // ToMarketplaceBuyPageCommand). Up to BuyCropRules.MaxOffersPerRun offers per run, each one chosen by
    // BuyCropRules.Select from a fresh read of the page.
    //
    // Facts from the real captures (TS12, Marketplace 10): accepting is direct (no confirm dialog); the accepted row
    // disappears and the free-merchant count drops by the merchants the offer needed; the success toast is not in the
    // DOM, so it is not used. The Accept button of an offer the village cannot take carries the CSS class "disabled".
    //
    // Failure policy (CLAUDE.md rule 4): this is a secondary task. Nothing here returns Retry or Stop; a failed step is a
    // Skip with a reason, and the caller (BuyCropTask) turns it into "look again later".
    [Handler]
    public static partial class BuyCropCommand
    {
        public sealed record Command(VillageId VillageId) : IVillageCommand;

        private const int ElementWaitSeconds = 8;
        private const int ConfirmWaitSeconds = 12;
        private const int PageChangeWaitSeconds = 10;
        private const int FilterWaitSeconds = 6;

        private const string SearchGroup = "#marketplaceBuy .filterWrapper > div:nth-child(2)";
        private const string RatioGroup = "#marketplaceBuy .filterWrapper > div:nth-child(4)";
        private const string ForwardButton = "#marketplaceBuy .pagination button.forward";

        private static async ValueTask<Result> HandleAsync(
            Command command,
            IChromeBrowser browser,
            AppDbContext context,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            try
            {
                return await RunAsync(command, browser, context, logger, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return Cancel.Error;
            }
        }

        private sealed record Found(MarketplaceOffer Offer, int FreeMerchants, bool Late, string Rejected);

        private static async ValueTask<Result> RunAsync(
            Command command,
            IChromeBrowser browser,
            AppDbContext context,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            var villageId = command.VillageId;
            var id = villageId.Value;

            var storage = context.Storages.FirstOrDefault(x => x.VillageId == id);
            if (storage is null || storage.Granary <= 0)
            {
                return Skip.Error.WithError("No storage data for the village yet");
            }

            var targetPercent = context.ByName(villageId, VillageSettingEnums.BuyCropTargetPercent);
            var maxMinutes = context.ByName(villageId, VillageSettingEnums.BuyCropMaxMinutes);
            var timeToEmpty = BuyCropRules.TimeToEmpty(storage.Crop, storage.FreeCrop);

            // what we pay with changes as we buy; the store fill decides which paying resource is preferred
            long wood = storage.Wood, clay = storage.Clay, iron = storage.Iron;
            double Fill(long value) => storage.Warehouse > 0 ? (double)value / storage.Warehouse : 0;

            await EnsureFiltersAsync(browser, logger, cancellationToken);

            var bought = 0;
            long boughtAmount = 0;
            var lastReason = "";

            for (var round = 0; round < BuyCropRules.MaxOffersPerRun; round++)
            {
                var pending = BuyCropMemory.PendingAmount(id);
                var need = BuyCropRules.Need(storage.Crop, pending, storage.Granary, targetPercent);
                if (need <= 0)
                {
                    logger.Information("Buy crop in {VillageId}: crop {Crop} + {Pending} on its way already reaches {Target}% of the granary - done.", villageId, storage.Crop, pending, targetPercent);
                    break;
                }

                var baseContext = new BuyCropRules.Context(
                    storage.Crop, pending, storage.Granary, targetPercent, timeToEmpty, maxMinutes, 0,
                    Fill(wood), Fill(clay), Fill(iron));

                var found = await ScanAsync(browser, logger, villageId, baseContext, cancellationToken);
                if (found is null)
                {
                    lastReason = "no usable offer";
                    break;
                }

                if (found.Late)
                {
                    logger.Warning(
                        "Buy crop in {VillageId}: no offer arrives before the crop runs out (about {Left:hh\\:mm\\:ss}) - taking the nearest one anyway.",
                        villageId, timeToEmpty ?? TimeSpan.Zero);
                }

                var offer = found.Offer;
                var accepted = await AcceptAsync(browser, logger, villageId, offer, found.FreeMerchants, cancellationToken);
                if (!accepted)
                {
                    lastReason = "the accept click was not confirmed";
                    break;
                }

                bought++;
                boughtAmount += offer.OfferedAmount;
                BuyCropMemory.AddPending(id, offer.OfferedAmount, BuyCropRules.Arrival(DateTime.Now, offer.Duration));
                switch (offer.WantedType)
                {
                    case "wood": wood -= offer.WantedAmount; break;
                    case "clay": clay -= offer.WantedAmount; break;
                    case "iron": iron -= offer.WantedAmount; break;
                }

                logger.Information(
                    "Buy crop in {VillageId}: bought {Offered} crop for {Wanted} {Type} ({Merchants} merchant(s), arrives in {Duration:hh\\:mm\\:ss}, from {Player}).",
                    villageId, offer.OfferedAmount, offer.WantedAmount, offer.WantedType, offer.Merchants, offer.Duration, offer.Player);

                await Task.Delay(750, cancellationToken);
            }

            var next = bought > 0
                ? BuyCropRules.NextCheck(DateTime.Now, BuyCropRules.BoughtMinMinutes, BuyCropRules.BoughtMaxMinutes, Random.Shared)
                : BuyCropRules.NextCheck(DateTime.Now, BuyCropRules.NothingBoughtMinMinutes, BuyCropRules.NothingBoughtMaxMinutes, Random.Shared);
            BuyCropMemory.SetNextCheck(id, next);

            if (bought > 0)
            {
                logger.Information("Buy crop in {VillageId}: {Count} offer(s), {Amount} crop on its way ({Pending} in total) - next look at {Next:HH:mm:ss}.", villageId, bought, boughtAmount, BuyCropMemory.PendingAmount(id), next);
            }
            else
            {
                logger.Information("Buy crop in {VillageId}: nothing bought ({Reason}) - next look at {Next:HH:mm:ss}.", villageId, string.IsNullOrEmpty(lastReason) ? "nothing needed" : lastReason, next);
            }

            return Result.Ok();
        }

        // Reads the offers of the page that is showing (and, when there is none usable, of the next pages) and
        // returns the offer to accept. Null = nothing usable.
        private static async Task<Found?> ScanAsync(
            IChromeBrowser browser,
            ILogger logger,
            VillageId villageId,
            BuyCropRules.Context baseContext,
            CancellationToken cancellationToken)
        {
            var rejected = new Dictionary<string, int>();

            for (var page = 1; page <= BuyCropRules.MaxPages; page++)
            {
                var html = browser.Html;
                var offers = MarketplaceBuyParser.GetOffers(html);
                var freeMerchants = MarketplaceBuyParser.GetFreeMerchants(html) ?? 0;

                if (freeMerchants <= 0)
                {
                    logger.Information("Buy crop in {VillageId}: no free merchants.", villageId);
                    return null;
                }

                var ctx = baseContext with { FreeMerchants = freeMerchants };
                var selection = BuyCropRules.Select(offers, ctx);
                foreach (var (reason, count) in selection.Rejected)
                {
                    rejected[reason] = rejected.GetValueOrDefault(reason) + count;
                }

                if (selection.Chosen is not null)
                {
                    return new Found(selection.Chosen, freeMerchants, selection.Late, BuyCropRules.RejectedText(rejected));
                }

                if (offers.Count == 0) break;

                // the list is sorted by trip time, so once a page ends beyond the limit the next ones are further still
                if (ctx.MaxMinutes > 0 && offers[^1].Duration > TimeSpan.FromMinutes(ctx.MaxMinutes)) break;

                var pagination = MarketplaceBuyParser.GetPagination(html);
                if (pagination is null || pagination.Value.Selected >= pagination.Value.Count || !MarketplaceBuyParser.IsForwardEnabled(html)) break;

                var selected = pagination.Value.Selected;
                if (!await ClickAsync(browser, ForwardButton, cancellationToken)) break;

                var changed = await WaitAsync(browser, PageChangeWaitSeconds, doc =>
                {
                    var p = MarketplaceBuyParser.GetPagination(doc);
                    return p is not null && p.Value.Selected > selected;
                }, cancellationToken);
                if (!changed)
                {
                    logger.Information("Buy crop in {VillageId}: the next page of offers did not show up.", villageId);
                    break;
                }
            }

            logger.Information("Buy crop in {VillageId}: no usable offer (left out: {Rejected}).", villageId, BuyCropRules.RejectedText(rejected));
            return null;
        }

        // Clicks Accept on the chosen row after making sure the row still holds the offer that was chosen.
        private static async Task<bool> AcceptAsync(
            IChromeBrowser browser,
            ILogger logger,
            VillageId villageId,
            MarketplaceOffer offer,
            int freeBefore,
            CancellationToken cancellationToken)
        {
            // The list re-renders and another player may take the offer between the read and the click: look again
            // right before clicking and do not trust the row number alone.
            var html = browser.Html;
            var row = MarketplaceBuyParser.GetOffers(html).FirstOrDefault(x => x.RowIndex == offer.RowIndex);
            if (row is null || row.Signature != offer.Signature || !row.Enabled)
            {
                logger.Information("Buy crop in {VillageId}: row {Row} changed before the click ({Offer}) - not clicking.", villageId, offer.RowIndex, offer);
                return false;
            }

            var countBefore = MarketplaceBuyParser.GetOffers(html).Count(x => x.Signature == offer.Signature);
            var css = $"#marketplaceBuy table.offers tbody tr:nth-child({offer.RowIndex}) td.accept button";
            var element = await FindElementAsync(browser, By.CssSelector(css), ElementWaitSeconds, cancellationToken);
            if (element is null)
            {
                logger.Warning("Buy crop in {VillageId}: the Accept button of row {Row} was not found ({Offer}).", villageId, offer.RowIndex, offer);
                return false;
            }

            var result = await browser.Click(element, cancellationToken);
            if (result.IsFailed)
            {
                logger.Warning("Buy crop in {VillageId}: clicking Accept failed: {Reason}", villageId, string.Join(" | ", result.Errors.Select(x => x.Message)));
                return false;
            }

            var confirmed = await WaitAsync(browser, ConfirmWaitSeconds, doc =>
            {
                var free = MarketplaceBuyParser.GetFreeMerchants(doc);
                if (free is not null && free.Value < freeBefore) return true;
                var count = MarketplaceBuyParser.GetOffers(doc).Count(x => x.Signature == offer.Signature);
                return MarketplaceBuyParser.GetContainer(doc) is not null && count < countBefore;
            }, cancellationToken);

            if (!confirmed)
            {
                logger.Warning("Buy crop in {VillageId}: could not confirm that {Offer} was accepted (free merchants {Free} did not drop, the row did not disappear).", villageId, offer, freeBefore);
                await PageDump.SaveAsync(browser, logger, "buycrop-unconfirmed");
                return false;
            }

            return true;
        }

        // The page remembers the filters of the last visit. Crop-only search and "1:1 or better" are wanted; when a
        // click does not take, the rules in BuyCropRules still protect (they re-check type and ratio per row).
        private static async Task EnsureFiltersAsync(IChromeBrowser browser, ILogger logger, CancellationToken cancellationToken)
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                var html = browser.Html;
                var onlyCrop = MarketplaceBuyParser.IsOnlyCropSearched(html);
                if (onlyCrop is null || onlyCrop.Value) break;

                var searchGroup = MarketplaceBuyParser.GetFilterGroups(html).FirstOrDefault();
                if (searchGroup is null) break;

                var wrong = searchGroup.Descendants("button").FirstOrDefault(x => (x.GetAttributeValue("data-option", "") == "4") != x.HasClass("active"));
                if (wrong is null) break;

                var option = wrong.GetAttributeValue("data-option", "");
                var wantActive = option == "4";
                if (!await ClickAsync(browser, $"{SearchGroup} button[data-option=\"{option}\"]", cancellationToken)) break;

                var ok = await WaitAsync(browser, FilterWaitSeconds, doc =>
                {
                    var button = MarketplaceBuyParser.GetFilterGroups(doc).FirstOrDefault()
                        ?.Descendants("button").FirstOrDefault(x => x.GetAttributeValue("data-option", "") == option);
                    return button is not null && button.HasClass("active") == wantActive;
                }, cancellationToken);
                if (!ok)
                {
                    logger.Warning("Buy crop: the search filter for option {Option} did not switch - continuing with the rules only.", option);
                    break;
                }
            }

            var ratio = MarketplaceBuyParser.IsRatioFilterActive(browser.Html);
            if (ratio == false)
            {
                if (await ClickAsync(browser, $"{RatioGroup} button", cancellationToken))
                {
                    var ok = await WaitAsync(browser, FilterWaitSeconds, doc => MarketplaceBuyParser.IsRatioFilterActive(doc) == true, cancellationToken);
                    if (!ok) logger.Warning("Buy crop: the 1:1 ratio filter did not switch on - continuing with the rules only.");
                }
            }

            await Task.Delay(500, cancellationToken);
        }

        private static async Task<bool> ClickAsync(IChromeBrowser browser, string css, CancellationToken cancellationToken)
        {
            var element = await FindElementAsync(browser, By.CssSelector(css), ElementWaitSeconds, cancellationToken);
            if (element is null) return false;
            var result = await browser.Click(element, cancellationToken);
            return result.IsSuccess;
        }

        // Polls for the element without the browser-wide 3 minute wait (same idea as SendResourceCommand).
        private static async Task<IWebElement?> FindElementAsync(IChromeBrowser browser, By by, int seconds, CancellationToken cancellationToken)
        {
            var driver = browser.Driver;
            if (driver is null) return null;

            var deadline = DateTime.UtcNow.AddSeconds(seconds);
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var elements = driver.FindElements(by);
                    if (elements.Count > 0 && elements[0].Displayed) return elements[0];
                }
                catch (BrowserException)
                {
                    // page re-rendered between lookup and check - just look again
                }

                if (DateTime.UtcNow >= deadline) return null;
                await Task.Delay(250, cancellationToken);
            }
        }

        // Waits for a page condition under a limit of its own; a user pause surfaces as OperationCanceledException.
        private static async Task<bool> WaitAsync(IChromeBrowser browser, int seconds, Func<HtmlDocument, bool> condition, CancellationToken cancellationToken)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(seconds));
            var result = await browser.Wait(driver =>
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(driver.PageSource);
                return condition(doc);
            }, cts.Token);
            cancellationToken.ThrowIfCancellationRequested();
            return result.IsSuccess;
        }
    }
}
