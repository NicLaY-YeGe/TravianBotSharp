namespace MainCore.Commands.Features.SendResource
{
    [Handler]
    public static partial class SendResourceCommand
    {
        // VillageId here is the SOURCE village (the one whose merchants will travel).
        // ClicksPerResource maps resource name ("wood"/"clay"/"iron"/"crop") to how many times
        // to click that resource's "+" button - each click sends exactly one merchant's worth.
        // This mirrors using the page by hand instead of typing raw numbers into the inputs,
        // which is more reliable because it goes through the site's own JS validation.
        public sealed record Command(VillageId VillageId, VillageId TargetVillageId, Dictionary<string, int> ClicksPerResource) : IVillageCommand;

        private static async ValueTask<Result> HandleAsync(
            Command command,
            IChromeBrowser browser,
            AppDbContext context,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            var (villageId, targetVillageId, clicksPerResource) = command;

            var totalClicks = clicksPerResource.Values.Sum();
            if (totalClicks <= 0) return Result.Ok();

            var targetVillage = context.Villages.FirstOrDefault(x => x.Id == targetVillageId.Value);
            if (targetVillage is null)
            {
                return Stop.Error.WithError($"Cannot find target village {targetVillageId} in the database.");
            }

            var freeMerchants = SendResourceParser.GetFreeMerchants(browser.Html);
            if (freeMerchants <= 0)
            {
                // Nothing to do right now - not an error, just try again on a later visit.
                logger.Information("No free merchants in village {VillageId} right now.", villageId);
                return Result.Ok();
            }

            // Never click more than we actually have merchants for, even if the caller asked
            // for more (defensive - the plan is computed slightly ahead of this live check).
            if (totalClicks > freeMerchants)
            {
                clicksPerResource = ScaleDown(clicksPerResource, freeMerchants);
                totalClicks = clicksPerResource.Values.Sum();
            }
            if (totalClicks <= 0) return Result.Ok();

            var result = await InputCoordinates(browser, targetVillage.X, targetVillage.Y, cancellationToken);
            if (result.IsFailed) return Stop.Error.WithErrors(result.Errors);

            // Entering the coordinates triggers an AJAX call that resolves the village name
            // and re-renders parts of the form. Give it a moment to settle before clicking
            // anything, otherwise the resource "+" buttons can briefly be missing/stale.
            var firstResourceType = clicksPerResource.Keys.First();
            result = await browser.Wait(driver =>
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(driver.PageSource);
                return SendResourceParser.GetPlusButton(doc, firstResourceType) is not null;
            }, cancellationToken);
            if (result.IsFailed) return Stop.Error.WithError("The send-resources form never finished loading after entering coordinates.");

            foreach (var (resourceType, clicks) in clicksPerResource)
            {
                for (var i = 0; i < clicks; i++)
                {
                    result = await ClickPlus(browser, resourceType, cancellationToken);
                    if (result.IsFailed) return Stop.Error.WithErrors(result.Errors);
                }
            }

            // Safety check: confirm the form actually has ONLY the intended resources filled
            // in before we commit to sending. If a click landed on the wrong element (the
            // page can shift under us), this catches it instead of shipping the wrong resource.
            result = await VerifyOnlyIntendedResourcesFilled(browser, clicksPerResource, cancellationToken);
            if (result.IsFailed) return Stop.Error.WithErrors(result.Errors);

            logger.Information(
                "Sending resources from village {VillageId} to ({X}|{Y}): {Plan}",
                villageId, targetVillage.X, targetVillage.Y,
                string.Join(", ", clicksPerResource.Where(x => x.Value > 0).Select(x => $"{x.Key}x{x.Value}")));

            result = await WaitSendButtonEnabled(browser, cancellationToken);
            if (result.IsFailed) return Stop.Error.WithError("Send button never became enabled - the amounts or target may not have registered.");

            result = await ClickSend(browser, cancellationToken);
            if (result.IsFailed) return Stop.Error.WithErrors(result.Errors);

            result = await WaitMerchantsDropped(browser, freeMerchants, cancellationToken);
            if (result.IsFailed) return Stop.Error.WithError("Merchant count did not drop after sending - the shipment may not have gone through.");

            logger.Information("Merchants sent.");

            return Result.Ok();
        }

        private static readonly string[] AllResourceTypes = ["wood", "clay", "iron", "crop"];

        private static async Task<Result> VerifyOnlyIntendedResourcesFilled(IChromeBrowser browser, Dictionary<string, int> clicksPerResource, CancellationToken cancellationToken)
        {
            foreach (var resourceType in AllResourceTypes)
            {
                var node = SendResourceParser.GetResourceInput(browser.Html, resourceType);
                if (node is null) continue;

                var (_, isFailed, element, errors) = await browser.GetElement(By.XPath(node.XPath), cancellationToken);
                if (isFailed) return Result.Fail(errors);

                // GetAttribute("value") returns the live input value (the JS-updated one),
                // not the original static HTML attribute - important, since a resource typed
                // in by the page's own JS won't necessarily be reflected in the raw page source.
                var value = (element.GetAttribute("value") ?? "0").ParseLong();
                var wasIntended = clicksPerResource.GetValueOrDefault(resourceType, 0) > 0;

                if (wasIntended && value <= 0)
                {
                    return Stop.Error.WithError($"Expected '{resourceType}' to have an amount filled in after clicking, but it shows 0 - a click may have missed.");
                }

                if (!wasIntended && value > 0)
                {
                    return Stop.Error.WithError($"'{resourceType}' unexpectedly has {value} filled in even though it wasn't part of the plan - a click likely landed on the wrong resource. Aborting instead of sending it.");
                }
            }

            return Result.Ok();
        }

        private static Dictionary<string, int> ScaleDown(Dictionary<string, int> clicksPerResource, int maxTotal)
        {
            var result = new Dictionary<string, int>();
            var remaining = maxTotal;
            foreach (var (resourceType, clicks) in clicksPerResource)
            {
                var take = Math.Min(clicks, remaining);
                if (take > 0)
                {
                    result[resourceType] = take;
                    remaining -= take;
                }
                if (remaining <= 0) break;
            }
            return result;
        }

        private static async Task<Result> InputCoordinates(IChromeBrowser browser, int x, int y, CancellationToken cancellationToken)
        {
            var xNode = SendResourceParser.GetXInput(browser.Html);
            if (xNode is null) return Retry.Error.WithError("Cannot find X coordinate input.");

            var (_, xFailed, xElement, xErrors) = await browser.GetElement(By.XPath(xNode.XPath), cancellationToken);
            if (xFailed) return Result.Fail(xErrors);

            var result = await browser.Input(xElement, $"{x}", cancellationToken);
            if (result.IsFailed) return result;

            var yNode = SendResourceParser.GetYInput(browser.Html);
            if (yNode is null) return Retry.Error.WithError("Cannot find Y coordinate input.");

            var (_, yFailed, yElement, yErrors) = await browser.GetElement(By.XPath(yNode.XPath), cancellationToken);
            if (yFailed) return Result.Fail(yErrors);

            return await browser.Input(yElement, $"{y}", cancellationToken);
        }

        private static async Task<Result> ClickPlus(IChromeBrowser browser, string resourceType, CancellationToken cancellationToken)
        {
            // The page can briefly re-render right after coordinates resolve (or after a
            // previous click updates the totals), so a single lookup can catch it mid-flicker.
            // Retry a few times internally before giving up, instead of failing on the first miss.
            const int maxAttempts = 5;
            Result lastError = Retry.Error.WithError($"Cannot find the '+' button for '{resourceType}'.");

            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                var node = SendResourceParser.GetPlusButton(browser.Html, resourceType);
                if (node is not null)
                {
                    var (_, isFailed, element, errors) = await browser.GetElement(By.XPath(node.XPath), cancellationToken);
                    if (!isFailed)
                    {
                        var clickResult = await browser.Click(element, cancellationToken);
                        if (clickResult.IsSuccess) return clickResult;
                        lastError = clickResult;
                    }
                    else
                    {
                        lastError = Result.Fail(errors);
                    }
                }

                if (attempt < maxAttempts)
                {
                    await Task.Delay(500, cancellationToken);
                }
            }

            return lastError;
        }

        private static async Task<Result> WaitSendButtonEnabled(IChromeBrowser browser, CancellationToken cancellationToken)
        {
            return await browser.Wait(driver =>
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(driver.PageSource);
                return SendResourceParser.IsSendButtonEnabled(doc);
            }, cancellationToken);
        }

        private static async Task<Result> WaitMerchantsDropped(IChromeBrowser browser, int freeMerchantsBefore, CancellationToken cancellationToken)
        {
            return await browser.Wait(driver =>
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(driver.PageSource);
                return SendResourceParser.GetFreeMerchants(doc) < freeMerchantsBefore;
            }, cancellationToken);
        }

        private static async Task<Result> ClickSend(IChromeBrowser browser, CancellationToken cancellationToken)
        {
            const int maxAttempts = 5;
            Result lastError = Retry.Error.WithError("Cannot find send button.");

            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                var node = SendResourceParser.GetSendButton(browser.Html);
                if (node is not null)
                {
                    var (_, isFailed, element, errors) = await browser.GetElement(By.XPath(node.XPath), cancellationToken);
                    if (!isFailed)
                    {
                        var clickResult = await browser.Click(element, cancellationToken);
                        if (clickResult.IsSuccess) return clickResult;
                        lastError = clickResult;
                    }
                    else
                    {
                        lastError = Result.Fail(errors);
                    }
                }

                if (attempt < maxAttempts)
                {
                    await Task.Delay(500, cancellationToken);
                }
            }

            return lastError;
        }
    }
}
