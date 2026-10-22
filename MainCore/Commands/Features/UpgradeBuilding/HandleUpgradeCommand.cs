namespace MainCore.Commands.Features.UpgradeBuilding
{
    [Handler]
    public static partial class HandleUpgradeCommand
    {
        public sealed record Command(VillageId VillageId, NormalBuildPlan Plan) : IVillageCommand;

        private static async ValueTask<Result> HandleAsync(
            Command command,
            IChromeBrowser browser,
            AppDbContext context,
            ILogger logger,
            CancellationToken cancellationToken
        )
        {
            var (villageId, plan) = command;

            var queueBuilding = context.QueueBuildings
                .Where(x => x.VillageId == villageId.Value)
                .Where(x => x.Location == plan.Location)
                .FirstOrDefault();

            if (queueBuilding is not null)
            {
                logger.Information("{Type} at location {Location} is in queue at level {Level}", queueBuilding.Type, queueBuilding.Location, queueBuilding.Level);
            }
            else
            {
                var building = context.Buildings
               .Where(x => x.VillageId == villageId.Value)
               .Where(x => x.Location == plan.Location)
               .FirstOrDefault();

                if (building is not null)
                {
                    logger.Information("{Type} at location {Location} is at level {Level}", building.Type, building.Location, building.Level);
                }
            }

            Result result;
            if (context.IsUpgradeable(villageId, plan))
            {
                var isSpecialUpgrade = context.BooleanByName(villageId, VillageSettingEnums.UseSpecialUpgrade);
                var isSpecialUpgradeable = context.IsSpecialUpgradeable(villageId, plan);
                if (isSpecialUpgrade && isSpecialUpgradeable)
                {
                    result = await browser.SpecialUpgrade(cancellationToken);
                    if (result.IsFailed) return result;
                }
                else
                {
                    result = await browser.Upgrade(cancellationToken);
                    if (result.IsFailed) return result;
                }
            }
            else
            {
                result = await browser.Construct(plan.Type, cancellationToken);
                if (result.IsFailed) return result;
            }

            return Result.Ok();
        }

        private static bool IsUpgradeable(this AppDbContext context, VillageId villageId, NormalBuildPlan plan)
        {
            return !context.IsEmptySite(villageId, plan.Location);
        }

        private static List<BuildingEnums> UnskippableBuildings = new()
        {
            BuildingEnums.Residence,
            BuildingEnums.Palace,
            BuildingEnums.CommandCenter,
        };

        private static bool IsSpecialUpgradeable(
            this AppDbContext context,
            VillageId villageId,
            NormalBuildPlan plan
        )
        {
            if (UnskippableBuildings.Contains(plan.Type)) return false;

            if (plan.Type.IsResourceField())
            {
                var getBuildingSpec = new GetBuildingSpec(villageId, plan.Location);
                var level = context.Buildings
                    .WithSpecification(getBuildingSpec)
                    .Select(x => x.Level)
                    .FirstOrDefault();
                if (level == 0) return false;
            }
            return true;
        }

        private static bool IsEmptySite(this AppDbContext context, VillageId villageId, int location)
        {
            return context.Buildings
                .Where(x => x.VillageId == villageId.Value)
                .Where(x => x.Location == location)
                .Where(x => x.Type == BuildingEnums.Site || x.Level == -1)
                .Any();
        }

        private static async Task<Result> SpecialUpgrade(
            this IChromeBrowser browser,
            CancellationToken cancellationToken
        )
        {
            var (_, isFailed, element, errors) = await browser.GetElement(doc => UpgradeParser.GetSpecialUpgradeButton(doc), cancellationToken);
            if (isFailed) return Result.Fail(errors);

            var result = await browser.Click(element, cancellationToken);
            if (result.IsFailed) return result;

            result = await browser.HandleAds(cancellationToken);
            if (result.IsFailed) return result;

            return Result.Ok();
        }

        private static async Task<Result> HandleAds(
            this IChromeBrowser browser,
            CancellationToken cancellationToken
        )
        {
            // 2026-10-18 (Playwright): close any ad pop-up tab/window, the main page stays active.
            var closeResult = await browser.CloseExtraTabs();
            if (closeResult.IsFailed) return Result.Fail(closeResult.Errors);

            static bool videoFeatureShown(IWebDriver driver)
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(driver.PageSource);
                return doc.GetElementbyId("videoFeature") is not null;
            }

            var result = await browser.Wait(videoFeatureShown, cancellationToken);
            if (result.IsFailed) return result;

            bool isFailed;
            IWebElement element;
            List<IError> errors;

            var videoFeature = browser.Html.GetElementbyId("videoFeature");
            if (videoFeature.HasClass("infoScreen"))
            {
                (_, isFailed, element, errors) = await browser.GetElement(doc => doc.GetElementbyId("videoFeature").Descendants("div").FirstOrDefault(x => x.HasClass("checkbox")), cancellationToken);
                if (isFailed) return Result.Fail(errors).WithError("Failed to find [Don't show watch ads confirm again] checkbox");

                result = await browser.Click(element, cancellationToken);
                if (result.IsFailed) return result;

                (_, isFailed, element, errors) = await browser.GetElement(doc => doc.GetElementbyId("videoFeature").Descendants("button").FirstOrDefault(x => x.HasClass("green")), cancellationToken);
                if (isFailed) return Result.Fail(errors).WithError("Failed to find [Watch ads] button");

                result = await browser.Click(element, cancellationToken);
                if (result.IsFailed) return result;
            }

            await Task.Delay(Random.Shared.Next(20_000, 25_000), CancellationToken.None);

            (_, isFailed, element, errors) = await browser.GetElement(doc => doc.GetElementbyId("videoFeature"), cancellationToken);
            if (isFailed) return Result.Fail(errors).WithError("Failed to find [Play ads video] button");

            result = await browser.Click(element, cancellationToken);
            if (result.IsFailed) return result;

            // close if bot click on playing ads
            // chrome will open new tab & pause ads
            // (2026-10-18, Playwright: no window-handle / iframe switching needed any more - the
            // main page stays the active one, the extra tabs are simply closed)
            do
            {
                closeResult = await browser.CloseExtraTabs();
                if (closeResult.IsFailed) return Result.Fail(closeResult.Errors);
                if (closeResult.Value == 0) break;

                (_, isFailed, element, errors) = await browser.GetElement(doc => doc.GetElementbyId("videoFeature"), cancellationToken);
                if (isFailed) return Result.Fail(errors).WithError("Failed to find [Play ads video] button");

                result = await browser.Click(element, cancellationToken);
                if (result.IsFailed) return result;
            }
            while (true);

            result = await browser.WaitPageChanged("dorf", cancellationToken);
            if (result.IsFailed) return result;

            await Task.Delay(Random.Shared.Next(5_000, 10_000), CancellationToken.None);

            var dontShowThisAgain = browser.Html.GetElementbyId("dontShowThisAgain");
            if (dontShowThisAgain is not null)
            {
                (_, isFailed, element, errors) = await browser.GetElement(By.XPath(dontShowThisAgain.StableXPath()), cancellationToken);
                if (isFailed) return Result.Fail(errors).WithError("Failed to find [Don't show this again] checkbox");

                result = await browser.Click(element, cancellationToken);
                if (result.IsFailed) return result;

                (_, isFailed, element, errors) = await browser.GetElement(doc => doc.DocumentNode.Descendants("button").FirstOrDefault(x => x.HasClass("dialogButtonOk")), cancellationToken);
                if (isFailed) return Result.Fail(errors).WithError("Failed to find [OK] button");

                result = await browser.Click(element, cancellationToken);
                if (result.IsFailed) return result;
            }

            return Result.Ok();
        }

        private static async Task<Result> Upgrade(
            this IChromeBrowser browser,
            CancellationToken cancellationToken)
        {
            var (_, isFailed, element, errors) = await browser.GetElement(doc => UpgradeParser.GetUpgradeButton(doc), cancellationToken);
            if (isFailed) return Result.Fail(errors);

            var result = await browser.Click(element, cancellationToken);
            if (result.IsFailed) return result;

            result = await browser.WaitPageChanged("dorf", cancellationToken);
            if (result.IsFailed) return result;

            return Result.Ok();
        }

        private static async Task<Result> Construct(
            this IChromeBrowser browser,
            BuildingEnums building,
            CancellationToken cancellationToken
        )
        {
            var (_, isFailed, element, errors) = await browser.GetElement(doc => UpgradeParser.GetConstructButton(doc, building), cancellationToken);
            if (isFailed) return Result.Fail(errors);

            var result = await browser.Click(element, cancellationToken);
            if (result.IsFailed) return result;

            result = await browser.WaitPageChanged("dorf", cancellationToken);
            if (result.IsFailed) return result;
            return Result.Ok();
        }
    }
}