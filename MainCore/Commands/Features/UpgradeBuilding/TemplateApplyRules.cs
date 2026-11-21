namespace MainCore.Commands.Features.UpgradeBuilding
{
    // 2026-11-21: when ApplyBuildTemplateTask may put the default template into a freshly founded village.
    //
    // Live logs (2026-11-20 and 2026-11-21) showed that the task ran BEFORE the village centre (dorf2) had been scanned: the layout
    // had 18 field rows and no centre row, so all 28 centre jobs were left out ("no building row for location ..."). The wait that
    // was meant to prevent this lived in CanStart, which nothing ever called for this task. The task now asks this rule itself.
    //
    // Pure so it is unit tested without the game types.
    public static class TemplateApplyRules
    {
        public enum Decision
        {
            // Nothing to do (setting off or the village already has a build list) - the task ends.
            Done,
            // The village centre has not been scanned yet - scan it and look again shortly.
            WaitForScan,
            // The scan did not arrive in time - the task ends with a warning.
            GiveUp,
            // Layout is complete - apply the template.
            Apply,
        }

        // 45 s between looks x 40 looks = 30 minutes at most.
        public const int MaxWaits = 40;
        public static readonly TimeSpan WaitInterval = TimeSpan.FromSeconds(45);

        // enabled: village setting AutoApplyBuildTemplateEnable (a village that never saved it counts as enabled - opt-out toggle).
        // centreScanned: the wall plot (location 40) has a Buildings row - dorf2 is scanned as a whole, so that row means all of it.
        public static Decision Decide(bool enabled, bool alreadyHasJobs, bool centreScanned, int waitsSoFar)
        {
            if (!enabled || alreadyHasJobs) return Decision.Done;
            if (centreScanned) return Decision.Apply;
            return waitsSoFar >= MaxWaits ? Decision.GiveUp : Decision.WaitForScan;
        }
    }
}
