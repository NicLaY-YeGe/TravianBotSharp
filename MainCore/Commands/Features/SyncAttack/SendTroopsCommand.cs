using MainCore.Commands.Features.DodgeTroop;

namespace MainCore.Commands.Features.SyncAttack
{
    // Generalizes DodgeTroop's SendReinforcementCommand: multiple troop slots (not just one),
    // an explicit target X/Y (not just a known village in our DB - the whole point of a rally
    // point send is you can hit ANY map coordinate), and any of the 3 event types.
    //
    // Confirm=false is a PROBE: fills the form and reads the server-computed "if sent now,
    // arrives at X" timestamp from the confirmation screen, then backs out WITHOUT clicking the
    // final confirm button. Travian doesn't reserve/deduct troops until that final click, so
    // backing out (we just navigate to the Rally Point overview tab) leaves the village
    // untouched - this lets SyncAttackPlanTask learn each village's real travel time (accounting
    // for server speed, tribe, artifacts, hero items - anything that affects it) without
    // guessing at a formula. Confirm=true does the exact same thing but actually sends.
    //
    // Both the "Send" click and the final "Confirm" click navigate the page via a
    // window.location.href-style onclick - per CLAUDE.md §2e, a Click() returning success only
    // means WebDriver ran the click, NOT that the server accepted the request (e.g. a stale
    // checksum token can be silently rejected). Both clicks are followed by an explicit
    // page-state check rather than being trusted at face value.
    [Handler]
    public static partial class SendTroopsCommand
    {
        public sealed record Command(
            VillageId VillageId,
            int X,
            int Y,
            RallyPointEventTypeEnums EventType,
            IReadOnlyDictionary<int, long> TroopAmounts,
            bool Confirm) : IVillageCommand;

        private static async ValueTask<Result<DateTime?>> HandleAsync(
            Command command,
            IChromeBrowser browser,
            ToRallyPointOverviewCommand.Handler toRallyPointOverviewCommand,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            var (villageId, x, y, eventType, troopAmounts, confirm) = command;

            foreach (var (slot, amount) in troopAmounts)
            {
                if (amount <= 0) continue;

                var available = RallyPointSendTroopsParser.GetAvailableTroopCount(browser.Html, slot);
                if (available < amount)
                {
                    return Retry.Error.WithError(
                        $"Village {villageId}: requested {amount} troops in slot {slot} but only {available} are available.");
                }

                var inputResult = await InputTroopAmount(browser, slot, amount, cancellationToken);
                if (inputResult.IsFailed) return Result.Fail(inputResult.Errors);
            }

            var coordResult = await InputCoordinates(browser, x, y, cancellationToken);
            if (coordResult.IsFailed) return Result.Fail(coordResult.Errors);

            var eventResult = await SelectEventType(browser, (int)eventType, cancellationToken);
            if (eventResult.IsFailed) return Result.Fail(eventResult.Errors);

            var sendResult = await ClickSend(browser, cancellationToken);
            if (sendResult.IsFailed) return Result.Fail(sendResult.Errors);

            // Verify the "Send" click actually took us to the confirmation screen (not a
            // silently-rejected request that leaves us on the same form) - see CLAUDE.md §2e.
            var waitResult = await browser.Wait(driver =>
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(driver.PageSource);
                return RallyPointSendTroopsParser.IsConfirmPage(doc);
            }, cancellationToken);
            if (waitResult.IsFailed)
            {
                return Stop.Error.WithErrors(waitResult.Errors)
                    .WithError($"Clicked send for village {villageId} but never reached the confirmation screen - the request likely wasn't accepted.");
            }

            DateTime? arrivalTime = null;
            var arrival = RallyPointSendTroopsParser.GetArrivalTimestamp(browser.Html);
            if (arrival is not null)
            {
                arrivalTime = DateTimeOffset.FromUnixTimeSeconds(arrival.Value).LocalDateTime;
            }

            if (!confirm)
            {
                logger.Information("Probed village {VillageId} -> ({X}|{Y}): would arrive at {ArrivalTime}.", villageId, x, y, arrivalTime);

                var backResult = await toRallyPointOverviewCommand.HandleAsync(new(villageId), cancellationToken);
                if (backResult.IsFailed) return Result.Fail(backResult.Errors);

                return arrivalTime;
            }

            var confirmResult = await ClickConfirm(browser, cancellationToken);
            if (confirmResult.IsFailed) return Result.Fail(confirmResult.Errors);

            // Verify the final "Confirm" click was actually accepted - per CLAUDE.md §2e this is
            // exactly the class of bug that made SmithyUpgradeCommand silently do nothing. A
            // successfully-committed send takes us OFF the confirmation screen; if we're still
            // looking at it, the click was rejected (stale checksum, no longer enough troops,
            // etc.) and nothing was actually sent.
            var confirmWaitResult = await browser.Wait(driver =>
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(driver.PageSource);
                return !RallyPointSendTroopsParser.IsConfirmPage(doc);
            }, cancellationToken);
            if (confirmWaitResult.IsFailed)
            {
                return Stop.Error.WithErrors(confirmWaitResult.Errors)
                    .WithError($"Clicked confirm for village {villageId} but the confirmation screen never went away - the send likely wasn't accepted.");
            }

            logger.Information("Sent troops from village {VillageId} to ({X}|{Y}), arriving {ArrivalTime}.", villageId, x, y, arrivalTime);

            return arrivalTime;
        }

        private static async Task<Result> InputTroopAmount(IChromeBrowser browser, int troopSlot, long amount, CancellationToken cancellationToken)
        {
            var node = RallyPointSendTroopsParser.GetTroopInput(browser.Html, troopSlot);
            if (node is null) return Retry.Error.WithError($"Cannot find troop slot {troopSlot} input.");

            var (_, isFailed, element, errors) = await browser.GetElement(By.XPath(node.XPath), cancellationToken);
            if (isFailed) return Result.Fail(errors);

            return await browser.Input(element, $"{amount}", cancellationToken);
        }

        private static async Task<Result> InputCoordinates(IChromeBrowser browser, int x, int y, CancellationToken cancellationToken)
        {
            var xNode = RallyPointSendTroopsParser.GetXInput(browser.Html);
            if (xNode is null) return Retry.Error.WithError("Cannot find X coordinate input.");

            var (_, xFailed, xElement, xErrors) = await browser.GetElement(By.XPath(xNode.XPath), cancellationToken);
            if (xFailed) return Result.Fail(xErrors);

            var result = await browser.Input(xElement, $"{x}", cancellationToken);
            if (result.IsFailed) return result;

            var yNode = RallyPointSendTroopsParser.GetYInput(browser.Html);
            if (yNode is null) return Retry.Error.WithError("Cannot find Y coordinate input.");

            var (_, yFailed, yElement, yErrors) = await browser.GetElement(By.XPath(yNode.XPath), cancellationToken);
            if (yFailed) return Result.Fail(yErrors);

            return await browser.Input(yElement, $"{y}", cancellationToken);
        }

        private static async Task<Result> SelectEventType(IChromeBrowser browser, int eventType, CancellationToken cancellationToken)
        {
            var node = RallyPointSendTroopsParser.GetEventTypeRadio(browser.Html, eventType);
            if (node is null) return Retry.Error.WithError($"Cannot find movement type option {eventType}.");

            var (_, isFailed, element, errors) = await browser.GetElement(By.XPath(node.XPath), cancellationToken);
            if (isFailed) return Result.Fail(errors);

            return await browser.Click(element, cancellationToken);
        }

        private static async Task<Result> ClickSend(IChromeBrowser browser, CancellationToken cancellationToken)
        {
            var node = RallyPointSendTroopsParser.GetSendButton(browser.Html);
            if (node is null) return Retry.Error.WithError("Cannot find the send button.");

            var (_, isFailed, element, errors) = await browser.GetElement(By.XPath(node.XPath), cancellationToken);
            if (isFailed) return Result.Fail(errors);

            return await browser.Click(element, cancellationToken);
        }

        private static async Task<Result> ClickConfirm(IChromeBrowser browser, CancellationToken cancellationToken)
        {
            var node = RallyPointSendTroopsParser.GetConfirmButton(browser.Html);
            if (node is null) return Retry.Error.WithError("Cannot find the confirm button.");

            var (_, isFailed, element, errors) = await browser.GetElement(By.XPath(node.XPath), cancellationToken);
            if (isFailed) return Result.Fail(errors);

            return await browser.Click(element, cancellationToken);
        }
    }
}
