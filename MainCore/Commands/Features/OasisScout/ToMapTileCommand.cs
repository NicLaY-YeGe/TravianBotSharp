using MainCore.Commands.Features.Settle;

namespace MainCore.Commands.Features.OasisScout
{
    // Opens the tile-detail dialog for an arbitrary map coordinate: navigate to the Map page,
    // type X/Y into the coordinate-jump form, click "Go", then click the map viewport itself -
    // the exact same 3-step sequence FoundNewVillageCommand.InputCoordinates already uses (see
    // MapParser's comments for why each step is needed: the map has no per-tile DOM element,
    // only the viewport, whose center is always whatever coordinate was just jumped to).
    //
    // Deliberately a SEPARATE, standalone command rather than extracting/sharing
    // FoundNewVillageCommand's private helper - there's no dotnet available in this delivery
    // process to verify a refactor of already-working, live-verified settle code doesn't
    // regress, so duplicating ~15 lines here is the safer trade (2026-09-15).
    [Handler]
    public static partial class ToMapTileCommand
    {
        public sealed record Command(int X, int Y) : ICommand;

        private static async ValueTask<r> HandleAsync(
            Command command,
            IChromeBrowser browser,
            IDelayService delayService,
            ToMapCommand.Handler toMapCommand,
            CancellationToken cancellationToken)
        {
            var (x, y) = command;

            var toMapResult = await toMapCommand.HandleAsync(new(), cancellationToken);
            if (toMapResult.IsFailed) return toMapResult;

            var (_, xFailed, xElement, xErrors) = await browser.GetElement(doc => MapParser.GetXInput(doc), cancellationToken);
            if (xFailed) return Result.Fail(xErrors);

            var result = await browser.Input(xElement, $"{x}", cancellationToken);
            if (result.IsFailed) return result;

            var (_, yFailed, yElement, yErrors) = await browser.GetElement(doc => MapParser.GetYInput(doc), cancellationToken);
            if (yFailed) return Result.Fail(yErrors);

            result = await browser.Input(yElement, $"{y}", cancellationToken);
            if (result.IsFailed) return result;

            var (_, goFailed, goElement, goErrors) = await browser.GetElement(doc => MapParser.GetGoButton(doc), cancellationToken);
            if (goFailed) return Result.Fail(goErrors);

            result = await browser.Click(goElement, cancellationToken);
            if (result.IsFailed) return result;

            // Same "no full page reload here, this is a JS/AJAX map shift - let it settle"
            // delay FoundNewVillageCommand's InputCoordinates already relies on for the exact
            // same click. See that command's comment for how this was confirmed live.
            await delayService.DelayClick(cancellationToken);

            var (_, mapFailed, mapElement, mapErrors) = await browser.GetElement(doc => MapParser.GetMapContainer(doc), cancellationToken);
            if (mapFailed) return Result.Fail(mapErrors);

            result = await browser.Click(mapElement, cancellationToken);
            if (result.IsFailed) return result;

            static bool TileDialogOpen(IWebDriver driver)
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(driver.PageSource);
                return OasisTileParser.IsTileDialogOpen(doc);
            }

            result = await browser.Wait(TileDialogOpen, cancellationToken);
            if (result.IsFailed)
            {
                return Retry.Error.WithError($"Clicked the map at ({x}|{y}) but the tile-detail dialog never opened.");
            }

            return Result.Ok();
        }
    }
}
