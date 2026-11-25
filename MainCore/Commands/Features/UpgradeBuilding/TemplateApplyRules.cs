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

        // A village has 18 resource fields (locations 1-18); the layout counts as scanned when all of them have a row.
        public const int ResourceFieldCount = 18;

        public static bool FieldsScanned(int fieldRowCount) => fieldRowCount >= ResourceFieldCount;

        // 45 s between looks x 40 looks = 30 minutes at most.
        public const int MaxWaits = 40;
        public static readonly TimeSpan WaitInterval = TimeSpan.FromSeconds(45);

        // enabled: village setting AutoApplyBuildTemplateEnable (a village that never saved it counts as enabled - opt-out toggle).
        // centreScanned: the wall plot (location 40) has a Buildings row - dorf2 is scanned as a whole, so that row means all of it.
        // fieldsScanned: the resource fields (dorf1, locations 1-18) have Buildings rows - dorf1 is a SEPARATE page and a village can
        // have the centre rows without them. 2026-11-23 (live log, village 47823): centre scanned, 0 field rows -> the template was applied
        // with 27 of 66 jobs, every resource-field job was dropped as "no field of this type left to pair with".
        public static Decision Decide(bool enabled, bool alreadyHasJobs, bool centreScanned, bool fieldsScanned, int waitsSoFar)
        {
            if (!enabled || alreadyHasJobs) return Decision.Done;
            if (centreScanned && fieldsScanned) return Decision.Apply;
            return waitsSoFar >= MaxWaits ? Decision.GiveUp : Decision.WaitForScan;
        }
    }
}
