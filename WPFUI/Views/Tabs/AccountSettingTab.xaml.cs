using MainCore.UI.ViewModels.Tabs;
using ReactiveUI;
using System.Reactive.Disposables.Fluent;

namespace WPFUI.Views.Tabs
{
    public class AccountSettingTabBase : ReactiveUserControl<AccountSettingViewModel>
    {
    }

    /// <summary>
    /// Interaction logic for AccountSettingTab.xaml
    /// </summary>
    public partial class AccountSettingTab : AccountSettingTabBase
    {
        public AccountSettingTab()
        {
            InitializeComponent();
            this.WhenActivated(d =>
            {
                this.BindCommand(ViewModel, vm => vm.ExportCommand, v => v.ExportButton).DisposeWith(d);
                this.BindCommand(ViewModel, vm => vm.ImportCommand, v => v.ImportButton).DisposeWith(d);
                this.BindCommand(ViewModel, vm => vm.SaveCommand, v => v.SaveButton).DisposeWith(d);

                this.Bind(ViewModel, vm => vm.AccountSettingInput.ClickDelay, v => v.ClickDelay.ViewModel).DisposeWith(d);
                this.Bind(ViewModel, vm => vm.AccountSettingInput.TaskDelay, v => v.TaskDelay.ViewModel).DisposeWith(d);
                this.Bind(ViewModel, vm => vm.AccountSettingInput.WorkTime, v => v.WorkTime.ViewModel).DisposeWith(d);
                this.Bind(ViewModel, vm => vm.AccountSettingInput.SleepTime, v => v.SleepTime.ViewModel).DisposeWith(d);
                this.Bind(ViewModel, vm => vm.AccountSettingInput.EnableAutoLoadVillage, v => v.EnableAutoLoadVillage.IsChecked).DisposeWith(d);
                this.Bind(ViewModel, vm => vm.AccountSettingInput.Tribe, v => v.Tribes.ViewModel).DisposeWith(d);
                this.Bind(ViewModel, vm => vm.AccountSettingInput.HeadlessChrome, v => v.HeadlessChrome.IsChecked).DisposeWith(d);
                this.Bind(ViewModel, vm => vm.AccountSettingInput.EnableAutoStartAdventure, v => v.EnableAutoStartAdventure.IsChecked).DisposeWith(d);
                this.Bind(ViewModel, vm => vm.AccountSettingInput.EnableClaimDailyQuestReward, v => v.EnableClaimDailyQuestReward.IsChecked).DisposeWith(d);
                this.Bind(ViewModel, vm => vm.AccountSettingInput.EnableAutoHeroRevive, v => v.EnableAutoHeroRevive.IsChecked).DisposeWith(d);
                this.Bind(ViewModel, vm => vm.AccountSettingInput.RaidSendGap, v => v.RaidSendGap.ViewModel).DisposeWith(d);
                this.Bind(ViewModel, vm => vm.AccountSettingInput.EnableRaidReport, v => v.EnableRaidReport.IsChecked).DisposeWith(d);
                this.Bind(ViewModel, vm => vm.AccountSettingInput.EnableCombatCheckOnRaidList, v => v.EnableCombatCheckOnRaidList.IsChecked).DisposeWith(d);
                this.Bind(ViewModel, vm => vm.AccountSettingInput.EnableCombatCheckOnAttack, v => v.EnableCombatCheckOnAttack.IsChecked).DisposeWith(d);
                this.OneWayBind(ViewModel, vm => vm.AccountSettingInput.CombatCheckMaxAgeHours, v => v.CombatCheckMaxAgeHours.ViewModel).DisposeWith(d);
                this.OneWayBind(ViewModel, vm => vm.AccountSettingInput.CombatSafetyMarginPercent, v => v.CombatSafetyMarginPercent.ViewModel).DisposeWith(d);
                this.Bind(ViewModel, vm => vm.AccountSettingInput.EnableAttackWake, v => v.EnableAttackWake.IsChecked).DisposeWith(d);
                this.Bind(ViewModel, vm => vm.AccountSettingInput.AttackWakeBefore, v => v.AttackWakeBefore.ViewModel).DisposeWith(d);
                this.Bind(ViewModel, vm => vm.AccountSettingInput.AttackWakeAfter, v => v.AttackWakeAfter.ViewModel).DisposeWith(d);
                this.OneWayBind(ViewModel, vm => vm.AccountSettingInput.MinHeroHealthPercent, v => v.MinHeroHealthPercent.ViewModel).DisposeWith(d);
                this.OneWayBind(ViewModel, vm => vm.AccountSettingInput.HammerVillageId, v => v.HammerVillageId.ViewModel).DisposeWith(d);
                this.OneWayBind(ViewModel, vm => vm.AccountSettingInput.HammerReservePercent, v => v.HammerReservePercent.ViewModel).DisposeWith(d);
                this.Bind(ViewModel, vm => vm.AccountSettingInput.EnableScoutAutoAttack, v => v.EnableScoutAutoAttack.IsChecked).DisposeWith(d);
                this.OneWayBind(ViewModel, vm => vm.AccountSettingInput.ScoutAutoAttackVillageId, v => v.ScoutAutoAttackVillageId.ViewModel).DisposeWith(d);
                this.OneWayBind(ViewModel, vm => vm.AccountSettingInput.ScoutAutoAttackType, v => v.ScoutAutoAttackType.ViewModel).DisposeWith(d);
                this.OneWayBind(ViewModel, vm => vm.AccountSettingInput.ScoutAutoAttackTroop1, v => v.ScoutAutoAttackTroop1.ViewModel).DisposeWith(d);
                this.OneWayBind(ViewModel, vm => vm.AccountSettingInput.ScoutAutoAttackTroop2, v => v.ScoutAutoAttackTroop2.ViewModel).DisposeWith(d);
                this.OneWayBind(ViewModel, vm => vm.AccountSettingInput.ScoutAutoAttackTroop3, v => v.ScoutAutoAttackTroop3.ViewModel).DisposeWith(d);
                this.OneWayBind(ViewModel, vm => vm.AccountSettingInput.ScoutAutoAttackTroop4, v => v.ScoutAutoAttackTroop4.ViewModel).DisposeWith(d);
                this.OneWayBind(ViewModel, vm => vm.AccountSettingInput.ScoutAutoAttackTroop5, v => v.ScoutAutoAttackTroop5.ViewModel).DisposeWith(d);
                this.OneWayBind(ViewModel, vm => vm.AccountSettingInput.ScoutAutoAttackTroop6, v => v.ScoutAutoAttackTroop6.ViewModel).DisposeWith(d);
                this.OneWayBind(ViewModel, vm => vm.AccountSettingInput.ScoutAutoAttackTroop7, v => v.ScoutAutoAttackTroop7.ViewModel).DisposeWith(d);
                this.OneWayBind(ViewModel, vm => vm.AccountSettingInput.ScoutAutoAttackTroop8, v => v.ScoutAutoAttackTroop8.ViewModel).DisposeWith(d);
                this.OneWayBind(ViewModel, vm => vm.AccountSettingInput.ScoutAutoAttackTroop9, v => v.ScoutAutoAttackTroop9.ViewModel).DisposeWith(d);
                this.OneWayBind(ViewModel, vm => vm.AccountSettingInput.ScoutAutoAttackTroop10, v => v.ScoutAutoAttackTroop10.ViewModel).DisposeWith(d);
                this.OneWayBind(ViewModel, vm => vm.AccountSettingInput.OnlineHours, v => v.OnlineHours.ViewModel).DisposeWith(d);

                this.Bind(ViewModel, vm => vm.TelegramBotToken, v => v.TelegramBotToken.Text).DisposeWith(d);
                this.Bind(ViewModel, vm => vm.TelegramChatId, v => v.TelegramChatId.Text).DisposeWith(d);
                this.Bind(ViewModel, vm => vm.TelegramNotifyOnAttack, v => v.TelegramNotifyOnAttack.IsChecked).DisposeWith(d);
                this.Bind(ViewModel, vm => vm.TelegramNotifyOnPause, v => v.TelegramNotifyOnPause.IsChecked).DisposeWith(d);
                this.BindCommand(ViewModel, vm => vm.TestTelegramCommand, v => v.TelegramTestButton).DisposeWith(d);
            });
        }
    }
}