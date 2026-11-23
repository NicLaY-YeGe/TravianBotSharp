namespace MainCore.Commands.Navigate
{
    [Handler]
    public static partial class ToDorfCommand
    {
        public sealed record Command(int Dorf) : ICommand;

        private static async ValueTask<Result> HandleAsync(
           Command command,
           IChromeBrowser browser,
           CancellationToken cancellationToken
           )
        {
            var dorf = command.Dorf;

            var currentUrl = browser.CurrentUrl;
            var currentDorf = GetCurrentDorf(currentUrl);
            if (dorf == 0)
            {
                if (currentDorf == 0) dorf = 2;
                else dorf = currentDorf;
            }

            if (currentDorf != 0 && dorf == currentDorf)
            {
                return Result.Ok();
            }

            var (_, isFailed, element, errors) = await browser.GetElement(doc => NavigationBarParser.GetDorfButton(doc, dorf), cancellationToken);
            if (isFailed) return Result.Fail(errors);

            Result result;
            result = await browser.Click(element, cancellationToken);
            if (result.IsFailed) return result;

            result = await browser.WaitPageChanged(url => dorf == 1 ? TravianUrl.IsDorf1(url) : TravianUrl.IsDorf2(url), $"dorf{dorf}", cancellationToken);
            if (result.IsFailed) return result;

            return Result.Ok();
        }

        private static int GetCurrentDorf(string url)
        {
            if (TravianUrl.IsDorf1(url)) return 1;
            if (TravianUrl.IsDorf2(url)) return 2;
            return 0;
        }
    }
}