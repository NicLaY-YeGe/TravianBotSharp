using MainCore.Commands.Features.UpgradeBuilding;

namespace MainCore.Test.Commands.Features.UpgradeBuilding
{
    public class TemplateApplyRulesTest
    {
        [Fact]
        public void Applies_WhenTheCentreIsScanned()
        {
            TemplateApplyRules.Decide(true, false, true, 0).ShouldBe(TemplateApplyRules.Decision.Apply);
            TemplateApplyRules.Decide(true, false, true, 25).ShouldBe(TemplateApplyRules.Decision.Apply);
        }

        [Fact]
        public void Waits_WhenTheCentreIsNotScannedYet()
        {
            TemplateApplyRules.Decide(true, false, false, 0).ShouldBe(TemplateApplyRules.Decision.WaitForScan);
            TemplateApplyRules.Decide(true, false, false, TemplateApplyRules.MaxWaits - 1).ShouldBe(TemplateApplyRules.Decision.WaitForScan);
        }

        [Fact]
        public void GivesUp_AfterTheMaximumNumberOfWaits()
        {
            TemplateApplyRules.Decide(true, false, false, TemplateApplyRules.MaxWaits).ShouldBe(TemplateApplyRules.Decision.GiveUp);
        }

        [Fact]
        public void IsDone_WhenDisabledOrTheVillageAlreadyHasJobs_WhateverTheScanState()
        {
            TemplateApplyRules.Decide(false, false, true, 0).ShouldBe(TemplateApplyRules.Decision.Done);
            TemplateApplyRules.Decide(false, false, false, 0).ShouldBe(TemplateApplyRules.Decision.Done);
            TemplateApplyRules.Decide(true, true, true, 0).ShouldBe(TemplateApplyRules.Decision.Done);
            TemplateApplyRules.Decide(true, true, false, 99).ShouldBe(TemplateApplyRules.Decision.Done);
        }
    }
}
