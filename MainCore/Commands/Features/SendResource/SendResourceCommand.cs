namespace MainCore.Commands.Features.SendResource
{
    [Handler]
    public static partial class SendResourceCommand
    {
        // VillageId here is the SOURCE village (the one whose merchants will travel).
        // Amounts maps resource name ("wood"/"clay"/"iron"/"crop") to the exact amount to
        // type into that resource's field. Callers are expected to have already rounded/
        // jittered these amounts - this command just fills the form and sends it.
        public sealed record Command(VillageId VillageId, VillageId TargetVillageId, Dictionary<string, long> Amounts) : IVillageCommand;

        private static readonly string[] AllResourceTypes = ["wood", "clay", "iron", "crop"];

        private static async ValueTask<Result> HandleAsync(
            Command command,
            IChromeBrowser browser,
            AppDbContext context,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            var (villageId, targetVillageId, amounts) = command;

            var totalRequested = amounts.Values.Sum();
            if (totalRequested <= 0) return Result.Ok();

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

            var capacity = SendResourceParser.GetMerchantCapacity(browser.Html);
            if (capacity <= 0) capacity = 1;

            // Never exceed what the merchants we actually have can carry, even if the
            // caller's plan asked for more (the plan is computed slightly ahead of this
            // live check, so free merchants may have changed).
            var maxTotal = (long)freeMerchants * capacity;
            if (totalRequested > maxTotal)
            {
                amounts = ScaleDown(amounts, maxTotal);
                totalRequested = amounts.Values.Sum();
            }
            if (totalRequested <= 0) return Result.Ok();

            // Clear all 4 resource fields first. The page can retain a leftover value from a
            // previous visit/session (same as it remembers the last-used target coordinates),
            // and we don't want to assume any of them start at 0.
            var clearResult = await ClearAllResourceInputs(browser, cancellationToken);
            if (clearResult.IsFailed) return Stop.Error.WithErrors(clearResult.Errors);

            var result = await InputCoordinates(browser, targetVillage.X, targetVillage.Y, cancellationToken);
            if (result.IsFailed) return Stop.Error.WithErrors(result.Errors);

            foreach (var (resourceType, amount) in amounts)
            {
                if (amount <= 0) continue;

                result = await TypeResourceAmount(browser, resourceType, amount, cancellationToken);
                if (result.IsFailed) return Stop.Error.WithErrors(result.Errors);
            }

            // Safety check: confirm the form actually has ONLY the intended resources filled
            // in before we commit to sending. If typing landed on the wrong element (the
            // page can shift under us), this catches it instead of shipping the wrong resource.
            result = await VerifyOnlyIntendedResourcesFilled(browser, amounts, cancellationToken);
            if (result.IsFailed) return Stop.Error.WithErrors(result.Errors);

            logger.Information(
                "Sending resources from village {VillageId} to ({X}|{Y}): {Plan}",
                villageId, targetVillage.X, targetVillage.Y,
                string.Join(", ", amounts.Where(x => x.Value > 0).Select(x => $"{x.Key}={x.Value}")));

            result = await WaitSendButtonEnabled(browser, cancellationToken);
            if (result.IsFailed) return Stop.Error.WithError("Send button never became enabled - the amounts or target may not have registered.");

            result = await ClickSend(browser, cancellationToken);
            if (result.IsFailed) return Stop.Error.WithErrors(result.Errors);

            // Live case (TS32, 2026-10-09): the form was filled correctly and the button enabled,
            // yet the mouse click never submitted (the button sits at the bottom edge of the
            // viewport next to the game's bottom bar, so the pointer click can land on an overlay).
            // If the merchant count hasn't moved shortly after, fall back to a JS click on the
            // same button - safe, because a click that did register shows up within a second or two.
            if (!await WaitMerchantsDroppedQuick(browser, freeMerchants, cancellationToken))
            {
                logger.Warning("Send click did not register - retrying with a script click.");
                var js = await ClickSendViaScript(browser, cancellationToken);
                if (js.IsFailed) return Stop.Error.WithErrors(js.Errors);
            }

            result = await WaitMerchantsDropped(browser, freeMerchants, cancellationToken);
            if (result.IsFailed)
            {
                // Even the script click did not move the counter (it could also be masked by
                // merchants returning from earlier shipments). Pausing the WHOLE account over a
                // small transfer is far worse than one missed/duplicated shipment (every caller
                // re-plans from live storage on its next run), so warn and carry on instead of Stop.
                logger.Warning("Could not confirm the shipment from village {VillageId} (free-merchant count did not drop within the wait) - continuing without pausing.", villageId);
                return Result.Ok();
            }

            logger.Information("Merchants sent.");

            return Result.Ok();
        }

        private static Dictionary<string, long> ScaleDown(Dictionary<string, long> amounts, long maxTotal)
        {
            var total = amounts.Values.Sum();
            if (total <= 0) return amounts;

            var result = new Dictionary<string, long>();
            foreach (var (resourceType, amount) in amounts)
            {
                result[resourceType] = amount * maxTotal / total;
            }
            return result;
        }

        private static async Task<Result> ClearAllResourceInputs(IChromeBrowser browser, CancellationToken cancellationToken)
        {
            foreach (var resourceType in AllResourceTypes)
            {
                var node = SendResourceParser.GetResourceInput(browser.Html, resourceType);
                if (node is null) continue;

                var (_, isFailed, element, errors) = await browser.GetElement(By.XPath(node.XPath), cancellationToken);
                if (isFailed) return Result.Fail(errors);

                var current = (element.GetAttribute("value") ?? "0").ParseLong();
                if (current == 0) continue;

                var result = await browser.Input(element, "0", cancellationToken);
                if (result.IsFailed) return result;
            }

            return Result.Ok();
        }

        private static async Task<Result> VerifyOnlyIntendedResourcesFilled(IChromeBrowser browser, Dictionary<string, long> amounts, CancellationToken cancellationToken)
        {
            foreach (var resourceType in AllResourceTypes)
            {
                var node = SendResourceParser.GetResourceInput(browser.Html, resourceType);
                if (node is null) continue;

                var (_, isFailed, element, errors) = await browser.GetElement(By.XPath(node.XPath), cancellationToken);
                if (isFailed) return Result.Fail(errors);

                var value = (element.GetAttribute("value") ?? "0").ParseLong();
                var intended = amounts.GetValueOrDefault(resourceType, 0);

                if (intended > 0 && value <= 0)
                {
                    return Stop.Error.WithError($"Expected '{resourceType}' to have an amount filled in after typing, but it shows 0 - the typing may have missed.");
                }

                if (intended <= 0 && value > 0)
                {
                    return Stop.Error.WithError($"'{resourceType}' unexpectedly has {value} filled in even though it wasn't part of the plan - typing likely landed on the wrong resource. Aborting instead of sending it.");
                }
            }

            return Result.Ok();
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

        private static async Task<Result> TypeResourceAmount(IChromeBrowser browser, string resourceType, long amount, CancellationToken cancellationToken)
        {
            var node = SendResourceParser.GetResourceInput(browser.Html, resourceType);
            if (node is null) return Retry.Error.WithError($"Cannot find '{resourceType}' amount input.");

            var (_, isFailed, element, errors) = await browser.GetElement(By.XPath(node.XPath), cancellationToken);
            if (isFailed) return Result.Fail(errors);

            return await browser.Input(element, $"{amount}", cancellationToken);
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
            var node = SendResourceParser.GetSendButton(browser.Html);
            if (node is null) return Retry.Error.WithError("Cannot find send button.");

            var (_, isFailed, element, errors) = await browser.GetElement(By.XPath(node.XPath), cancellationToken);
            if (isFailed) return Result.Fail(errors);

            // Bring the button to the middle of the viewport first so nothing (bottom bar, cookie
            // icon) overlaps it, then click.
            await browser.ExecuteJsScript(ScrollIntoViewScript(node.XPath));
            return await browser.Click(element, cancellationToken);
        }

        private static async Task<Result> ClickSendViaScript(IChromeBrowser browser, CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            var node = SendResourceParser.GetSendButton(browser.Html);
            if (node is null) return Retry.Error.WithError("Cannot find send button.");

            var xpath = node.XPath.Replace("\\", "\\\\").Replace("\"", "\\\"");
            return await browser.ExecuteJsScript(
                $"var el=document.evaluate(\"{xpath}\",document,null,XPathResult.FIRST_ORDERED_NODE_TYPE,null).singleNodeValue;" +
                "if(el){el.scrollIntoView({block:'center'});el.click();}");
        }

        private static string ScrollIntoViewScript(string xpathRaw)
        {
            var xpath = xpathRaw.Replace("\\", "\\\\").Replace("\"", "\\\"");
            return $"var el=document.evaluate(\"{xpath}\",document,null,XPathResult.FIRST_ORDERED_NODE_TYPE,null).singleNodeValue;" +
                   "if(el){el.scrollIntoView({block:'center'});}";
        }

        // Polls the live page for up to ~10 seconds; true as soon as the free-merchant count drops.
        private static async Task<bool> WaitMerchantsDroppedQuick(IChromeBrowser browser, int freeMerchantsBefore, CancellationToken cancellationToken)
        {
            for (var i = 0; i < 10; i++)
            {
                if (cancellationToken.IsCancellationRequested) return true;
                await Task.Delay(1000, cancellationToken);
                if (SendResourceParser.GetFreeMerchants(browser.Html) < freeMerchantsBefore) return true;
            }
            return false;
        }
    }
}
