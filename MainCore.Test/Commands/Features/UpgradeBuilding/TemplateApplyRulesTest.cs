using MainCore.Commands.Features.UpgradeBuilding;

namespace MainCore.Test.Commands.Features.UpgradeBuilding
{
    public class TemplateApplyRulesTest
    {
        [Fact]
        public void Applies_WhenTheCentreAndTheFieldsAreScanned()
        {
            TemplateApplyRules.Decide(true, false, true, true, 0).ShouldBe(TemplateApplyRules.Decision.Apply);
            TemplateApplyRules.Decide(true, false, true, true, 25).ShouldBe(TemplateApplyRules.Decision.Apply);
        }

        [Fact]
        public void Waits_WhenOnlyTheCentreIsScanned_TheFieldsAreMissing()
        {
            // 2026-11-23 live log, village 47823: centre scanned, 0 field rows.
            TemplateApplyRules.Decide(true, false, true, false, 0).ShouldBe(TemplateApplyRules.Decision.WaitForScan);
            TemplateApplyRules.Decide(true, false, true, false, TemplateApplyRules.MaxWaits).ShouldBe(TemplateApplyRules.Decision.GiveUp);
        }

        [Fact]
        public void Waits_WhenOnlyTheFieldsAreScanned_TheCentreIsMissing()
        {
            TemplateApplyRules.Decide(true, false, false, true, 0).ShouldBe(TemplateApplyRules.Decision.WaitForScan);
        }

        [Theory]
        [InlineData(0, false)]
        [InlineData(17, false)]
        [InlineData(18, true)]
        [InlineData(22, true)]
        public void FieldsScanned_NeedsAllEighteenFieldRows(int rows, bool expected)
        {
            TemplateApplyRules.FieldsScanned(rows).ShouldBe(expected);
        }

        [Fact]
        public void Waits_WhenTheCentreIsNotScannedYet()
        {
            TemplateApplyRules.Decide(true, false, false, false, 0).ShouldBe(TemplateApplyRules.Decision.WaitForScan);
            TemplateApplyRules.Decide(true, false, false, false, TemplateApplyRules.MaxWaits - 1).ShouldBe(TemplateApplyRules.Decision.WaitForScan);
        }

        [Fact]
        public void GivesUp_AfterTheMaximumNumberOfWaits()
        {
            TemplateApplyRules.Decide(true, false, false, false, TemplateApplyRules.MaxWaits).ShouldBe(TemplateApplyRules.Decision.GiveUp);
        }

        [Fact]
        public void IsDone_WhenDisabledOrTheVillageAlreadyHasJobs_WhateverTheScanState()
        {
            TemplateApplyRules.Decide(false, false, true, true, 0).ShouldBe(TemplateApplyRules.Decision.Done);
            TemplateApplyRules.Decide(false, false, false, false, 0).ShouldBe(TemplateApplyRules.Decision.Done);
            TemplateApplyRules.Decide(true, true, true, true, 0).ShouldBe(TemplateApplyRules.Decision.Done);
            TemplateApplyRules.Decide(true, true, false, false, 99).ShouldBe(TemplateApplyRules.Decision.Done);
        }
    }
}
