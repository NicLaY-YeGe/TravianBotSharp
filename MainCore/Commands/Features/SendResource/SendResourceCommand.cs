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

        // How long we wait for a form element to become usable. The browser-wide default wait is
        // 3 minutes (ad-friendly), which turned one unusable input into a 12-minute stall plus an
        // account pause (live case TS32, 2026-10-14, Balance resources in TS-3203). The send form
        // is already loaded by the time we get here, so a few seconds is plenty.
        private const int ElementWaitSeconds = 8;

        private static async ValueTask<Result> HandleAsync(
            Command command,
            IChromeBrowser browser,
            AppDbContext context,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            try
            {
                return await SendAsync(command, browser, context, logger, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return Cancel.Error;
            }
        }

        // A failure to fill/submit the send form is never worth pausing the whole account: every
        // caller re-plans from live storage on its next run (see CLAUDE.md 2l). So these are Skip,
        // with enough detail in the log to tell which step/resource/XPath was the problem.
        private static Result FormFailure(ILogger logger, Command command, string step, string detail)
        {
            var plan = string.Join(", ", command.Amounts.Where(x => x.Value > 0).Select(x => $"{x.Key}={x.Value}"));
            var message = $"Send form failed at step '{step}' ({detail}); source {command.VillageId} -> target {command.TargetVillageId}; plan: {plan}";
            logger.Warning(message);
            return Skip.Error.WithError(message);
        }

        // Polls for an element without the browser-wide 3 minute wait. requireInteractable=false
        // is for read-only checks (an input that is hidden/disabled still has a readable value).
        private static async Task<IWebElement?> FindElement(IChromeBrowser browser, string xpath, int seconds, bool requireInteractable, CancellationToken cancellationToken)
        {
            var driver = browser.Driver;
            if (driver is null) return null;

            var deadline = DateTime.UtcNow.AddSeconds(seconds);
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var elements = driver.FindElements(By.XPath(xpath));
                    if (elements.Count > 0)
                    {
                        var element = elements[0];
                        if (!requireInteractable || (element.Displayed && element.Enabled)) return element;
                    }
                }
                catch (BrowserException)
                {
                    // page re-rendered between lookup and check - just look again
                }

                if (DateTime.UtcNow >= deadline) return null;
                await Task.Delay(250, cancellationToken);
            }
        }

        private static async ValueTask<Result> SendAsync(
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
                return FormFailure(logger, command, "lookup target village", "target village not found in the database");
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
            var clearResult = await ClearAllResourceInputs(browser, logger, command, cancellationToken);
            if (clearResult.IsFailed) return clearResult;

            var result = await InputCoordinates(browser, logger, command, targetVillage.X, targetVillage.Y, cancellationToken);
            if (result.IsFailed) return result;

            foreach (var (resourceType, amount) in amounts)
            {
                if (amount <= 0) continue;

                result = await TypeResourceAmount(browser, logger, command, resourceType, amount, cancellationToken);
                if (result.IsFailed) return result;
            }

            // Safety check: confirm the form actually has ONLY the intended resources filled
            // in before we commit to sending. If typing landed on the wrong element (the
            // page can shift under us), this catches it instead of shipping the wrong resource.
            result = await VerifyOnlyIntendedResourcesFilled(browser, logger, command, amounts, cancellationToken);
            if (result.IsFailed) return result;

            logger.Information(
                "Sending resources from village {VillageId} to ({X}|{Y}): {Plan}",
                villageId, targetVillage.X, targetVillage.Y,
                string.Join(", ", amounts.Where(x => x.Value > 0).Select(x => $"{x.Key}={x.Value}")));

            result = await WaitSendButtonEnabled(browser, cancellationToken);
            if (result.IsFailed) return FormFailure(logger, command, "wait send button", "send button never became enabled - the amounts or target may not have registered");

            result = await ClickSend(browser, logger, command, cancellationToken);
            if (result.IsFailed) return result;

            // Live case (TS32, 2026-10-09): the form was filled correctly and the button enabled,
            // yet the mouse click never submitted (the button sits at the bottom edge of the
            // viewport next to the game's bottom bar, so the pointer click can land on an overlay).
            // If the merchant count hasn't moved shortly after, fall back to a JS click on the
            // same button - safe, because a click that did register shows up within a second or two.
            if (!await WaitMerchantsDroppedQuick(browser, freeMerchants, cancellationToken))
            {
                logger.Warning("Send click did not register - retrying with a script click.");
                var js = await ClickSendViaScript(browser, cancellationToken);
                if (js.IsFailed) return FormFailure(logger, command, "script click send", "send button not found");
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

        private static async Task<Result> ClearAllResourceInputs(IChromeBrowser browser, ILogger logger, Command command, CancellationToken cancellationToken)
        {
            foreach (var resourceType in AllResourceTypes)
            {
                var node = SendResourceParser.GetResourceInput(browser.Html, resourceType);
                if (node is null) continue;

                // Don't require the input to be usable: Travian can render a resource's input
                // hidden/disabled (e.g. nothing of it to send), and waiting for that to become
                // interactable is what stalled the bot for 3 minutes. Nothing to clear there.
                var element = await FindElement(browser, node.XPath, 0, requireInteractable: false, cancellationToken);
                if (element is null) continue;

                var current = (element.GetAttribute("value") ?? "0").ParseLong();
                if (current == 0) continue;

                if (!element.Displayed || !element.Enabled)
                {
                    return FormFailure(logger, command, $"clear {resourceType}", $"input holds {current} but is not interactable ({node.XPath})");
                }

                var result = await browser.Input(element, "0", cancellationToken);
                if (result.IsFailed) return FormFailure(logger, command, $"clear {resourceType}", $"typing failed ({node.XPath})");
            }

            return Result.Ok();
        }

        private static async Task<Result> VerifyOnlyIntendedResourcesFilled(IChromeBrowser browser, ILogger logger, Command command, Dictionary<string, long> amounts, CancellationToken cancellationToken)
        {
            foreach (var resourceType in AllResourceTypes)
            {
                var node = SendResourceParser.GetResourceInput(browser.Html, resourceType);
                if (node is null) continue;

                var intended = amounts.GetValueOrDefault(resourceType, 0);

                var element = await FindElement(browser, node.XPath, 0, requireInteractable: false, cancellationToken);
                if (element is null)
                {
                    // An input we meant to fill must exist; one we didn't touch just isn't there to hold a value.
                    if (intended > 0) return FormFailure(logger, command, $"verify {resourceType}", $"input not found ({node.XPath})");
                    continue;
                }

                var value = (element.GetAttribute("value") ?? "0").ParseLong();

                if (intended > 0 && value <= 0)
                {
                    return FormFailure(logger, command, $"verify {resourceType}", "shows 0 after typing - the typing may have missed");
                }

                if (intended <= 0 && value > 0)
                {
                    return FormFailure(logger, command, $"verify {resourceType}", $"unexpectedly has {value} filled in though it wasn't part of the plan - typing likely landed on the wrong resource, aborting instead of sending");
                }
            }

            return Result.Ok();
        }

        private static async Task<Result> InputCoordinates(IChromeBrowser browser, ILogger logger, Command command, int x, int y, CancellationToken cancellationToken)
        {
            var xNode = SendResourceParser.GetXInput(browser.Html);
            if (xNode is null) return FormFailure(logger, command, "coordinates", "X input not in page");

            var xElement = await FindElement(browser, xNode.XPath, ElementWaitSeconds, requireInteractable: true, cancellationToken);
            if (xElement is null) return FormFailure(logger, command, "coordinates", $"X input not usable ({xNode.XPath})");

            var result = await browser.Input(xElement, $"{x}", cancellationToken);
            if (result.IsFailed) return FormFailure(logger, command, "coordinates", "typing X failed");

            var yNode = SendResourceParser.GetYInput(browser.Html);
            if (yNode is null) return FormFailure(logger, command, "coordinates", "Y input not in page");

            var yElement = await FindElement(browser, yNode.XPath, ElementWaitSeconds, requireInteractable: true, cancellationToken);
            if (yElement is null) return FormFailure(logger, command, "coordinates", $"Y input not usable ({yNode.XPath})");

            result = await browser.Input(yElement, $"{y}", cancellationToken);
            return result.IsFailed ? FormFailure(logger, command, "coordinates", "typing Y failed") : result;
        }

        private static async Task<Result> TypeResourceAmount(IChromeBrowser browser, ILogger logger, Command command, string resourceType, long amount, CancellationToken cancellationToken)
        {
            var step = $"type {resourceType}={amount}";

            var node = SendResourceParser.GetResourceInput(browser.Html, resourceType);
            if (node is null) return FormFailure(logger, command, step, "input not in page");

            var element = await FindElement(browser, node.XPath, ElementWaitSeconds, requireInteractable: true, cancellationToken);
            if (element is null) return FormFailure(logger, command, step, $"input not displayed/enabled within {ElementWaitSeconds}s ({node.XPath})");

            var result = await browser.Input(element, $"{amount}", cancellationToken);
            return result.IsFailed ? FormFailure(logger, command, step, "typing failed") : result;
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

        private static async Task<Result> ClickSend(IChromeBrowser browser, ILogger logger, Command command, CancellationToken cancellationToken)
        {
            var node = SendResourceParser.GetSendButton(browser.Html);
            if (node is null) return FormFailure(logger, command, "click send", "send button not in page");

            var element = await FindElement(browser, node.XPath, ElementWaitSeconds, requireInteractable: true, cancellationToken);
            if (element is null) return FormFailure(logger, command, "click send", $"send button not usable ({node.XPath})");

            // Bring the button to the middle of the viewport first so nothing (bottom bar, cookie
            // icon) overlaps it, then click.
            await browser.ExecuteJsScript(ScrollIntoViewScript(node.XPath));
            var result = await browser.Click(element, cancellationToken);
            return result.IsFailed ? FormFailure(logger, command, "click send", "click failed") : result;
        }

        private static async Task<Result> ClickSendViaScript(IChromeBrowser browser, CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            var node = SendResourceParser.GetSendButton(browser.Html);
            if (node is null) return Skip.Error.WithError("Cannot find send button.");

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
