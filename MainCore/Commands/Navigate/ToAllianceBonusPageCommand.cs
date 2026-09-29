namespace MainCore.Commands.Navigate
{
    // /alliance/bonuses has no building/gid and isn't reachable via ToBuildingByTypeCommand -
    // it's a top-level account page (sidebar "Alliance" button), so it's navigated to directly
    // by URL, same pattern as the wall's special-case host-relative Navigate in
    // ToBuildingByLocationCommand.
    [Handler]
    public static partial class ToAllianceBonusPageCommand
    {
        public sealed record Command : ICommand;

        private static async ValueTask<Result> HandleAsync(
           Command command,
           IChromeBrowser browser,
           CancellationToken cancellationToken
           )
        {
            var currentUrl = new Uri(browser.CurrentUrl);
            var host = currentUrl.GetLeftPart(UriPartial.Authority);

            var result = await browser.Navigate($"{host}/alliance/bonuses", cancellationToken);
            if (result.IsFailed) return result;

            result = await browser.WaitPageChanged("alliance/bonuses", cancellationToken);
            if (result.IsFailed) return result;

            return Result.Ok();
        }
    }
}
