namespace MainCore.Commands.Features.UpgradeBuilding
{
    // 2026-10-28 (live log A_1202, 17:25): the plan "Brickyard at slot 20 to level 5" met a slot that really holds a
    // Marketplace (level 13). ValidatePlanCompleteCommand only compared types when they matched, so a different building
    // fell through to the "construct" path, HandleUpgradeCommand clicked Upgrade on whatever stood in the slot, and
    // 1800 wood / 600 clay / 1700 iron from the hero inventory went into Marketplace level 14 (the requested cost 1980/1735/2970
    // is exactly Marketplace 14, not Brickyard 5). The same plan would have repeated on the next run.
    //
    // A plan is only valid for a slot that is empty (Site / level -1), already holds the plan's own building, or has the plan's
    // building under construction there. Anything else is a plan for the wrong slot. Pure so it is unit tested.
    public static class BuildPlanSlotRules
    {
        public static bool IsOccupiedByOtherBuilding(BuildingEnums actualType, int actualLevel, BuildingEnums planType, bool planTypeQueuedHere)
        {
            if (actualType == planType) return false;
            if (actualType == BuildingEnums.Site || actualType == BuildingEnums.Unknown) return false;
            if (actualLevel < 0) return false;
            if (planTypeQueuedHere) return false;
            return true;
        }
    }
}
