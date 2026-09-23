#pragma warning disable S1172

namespace MainCore.Commands.Features.Trap
{
    // Same shape as TrainTroopCommand (pick an amount, type it into the input, click the
    // submit button) with one extra cap that troops don't have: a Trapper at its CURRENT
    // level can only ever hold TrapParser.GetMaxPossibleTraps traps - that cap only rises when
    // the building itself levels up (a separate, untouched upgrade flow), so a batch that
    // would push past it is trimmed down rather than rejected outright.
    [Handler]
    public static partial class BuildTrapsCommand
    {
        public sealed record Command(VillageId VillageId) : IVillageCommand;

        private static async ValueTask<Result> HandleAsync(
            Command command,
            AppDbContext context,
            IChromeBrowser browser,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            var villageId = command.VillageId;
            var doc = browser.Html;

            var maxPossible = TrapParser.GetMaxPossibleTraps(doc);
            var current = TrapParser.GetCurrentTrapCount(doc);
            var remainingCapacity = maxPossible - current;
            if (remainingCapacity <= 0)
            {
                logger.Information("Trapper is already at its current level's cap ({Max} traps) - nothing to build.", maxPossible);
                return Result.Ok();
            }

            var target = context.ByName(villageId, VillageSettingEnums.TrapAmountMin, VillageSettingEnums.TrapAmountMax);
            var amount = Math.Min(target, remainingCapacity);

            var maxAffordable = TrapParser.GetMaxAffordableNow(doc);
            if (amount > maxAffordable)
            {
                var trainWhenLowResource = context.BooleanByName(villageId, VillageSettingEnums.TrainWhenLowResource);
                if (!trainWhenLowResource)
                {
                    return MissingResource.Error("Trap", maxAffordable, amount);
                }
                amount = maxAffordable;
            }

            if (amount <= 0)
            {
                return MissingResource.Error("Trap", maxAffordable, target);
            }

            var result = await Build(browser, amount, cancellationToken);
            if (result.IsFailed) return result;

            logger.Information("Building {Amount} trap(s) ({Current}/{Max} before this batch).", amount, current, maxPossible);
            return Result.Ok();
        }

        private static async ValueTask<Result> Build(
            IChromeBrowser browser,
            long amount,
            CancellationToken cancellationToken)
        {
            var (_, isFailed, element, errors) = await browser.GetElement(TrapParser.GetInputBox, cancellationToken);
            if (isFailed) return Result.Fail(errors);

            var result = await browser.Input(element, $"{amount}", cancellationToken);
            if (result.IsFailed) return result;

            (_, isFailed, element, errors) = await browser.GetElement(TrapParser.GetBuildButton, cancellationToken);
            if (isFailed) return Result.Fail(errors);

            result = await browser.Click(element, cancellationToken);
            if (result.IsFailed) return result;

            return Result.Ok();
        }
    }
}
