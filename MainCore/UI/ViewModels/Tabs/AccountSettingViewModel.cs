using MainCore.Commands.UI.Misc;
using MainCore.UI.Models.Input;
using MainCore.UI.Models.Output;
using MainCore.UI.ViewModels.Abstract;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace MainCore.UI.ViewModels.Tabs
{
    [RegisterSingleton<AccountSettingViewModel>]
    public partial class AccountSettingViewModel : AccountTabViewModelBase
    {
        public AccountSettingInput AccountSettingInput { get; } = new();

        private readonly IDialogService _dialogService;
        private readonly IValidator<AccountSettingInput> _accountsettingInputValidator;
        private readonly ICustomServiceScopeFactory _serviceScopeFactory;
        private readonly ITelegramNotifier _telegramNotifier;

        [Reactive]
        private string _telegramBotToken = "";

        [Reactive]
        private string _telegramChatId = "";

        [Reactive]
        private bool _telegramNotifyOnAttack = true;

        [Reactive]
        private bool _telegramNotifyOnPause = true;

        public AccountSettingViewModel(IDialogService dialogService, IValidator<AccountSettingInput> accountsettingInputValidator, ICustomServiceScopeFactory serviceScopeFactory, ITelegramNotifier telegramNotifier)
        {
            _dialogService = dialogService;
            _accountsettingInputValidator = accountsettingInputValidator;
            _serviceScopeFactory = serviceScopeFactory;
            _telegramNotifier = telegramNotifier;

            LoadSettingsCommand.Subscribe(AccountSettingInput.Set);
        }

        protected override async Task Load(AccountId accountId)
        {
            await LoadSettingsCommand.Execute(accountId);

            var telegramSetting = _telegramNotifier.Get(accountId);
            TelegramBotToken = telegramSetting.BotToken;
            TelegramChatId = telegramSetting.ChatId;
            TelegramNotifyOnAttack = telegramSetting.NotifyOnAttack;
            TelegramNotifyOnPause = telegramSetting.NotifyOnPause;
        }

        [ReactiveCommand]
        private async Task TestTelegram()
        {
            _telegramNotifier.Save(AccountId, new TelegramAccountSetting
            {
                BotToken = TelegramBotToken,
                ChatId = TelegramChatId,
                NotifyOnAttack = TelegramNotifyOnAttack,
                NotifyOnPause = TelegramNotifyOnPause,
            });

            await _telegramNotifier.NotifyAsync(AccountId, "\u2705 TravianBotSharp test message.");
            await _dialogService.MessageBox.Handle(new MessageBoxData("Information", "Test message sent (check your Telegram chat)."));
        }

        [ReactiveCommand]
        private async Task Save()
        {
            var result = await _accountsettingInputValidator.ValidateAsync(AccountSettingInput);
            if (!result.IsValid)
            {
                await _dialogService.MessageBox.Handle(new MessageBoxData("Error", result.ToString()));
                return;
            }

            using var scope = _serviceScopeFactory.CreateScope(AccountId);
            var saveAccountSettingCommand = scope.ServiceProvider.GetRequiredService<SaveAccountSettingCommand.Handler>();
            await saveAccountSettingCommand.HandleAsync(new(AccountId, AccountSettingInput.Get()));

            _telegramNotifier.Save(AccountId, new TelegramAccountSetting
            {
                BotToken = TelegramBotToken,
                ChatId = TelegramChatId,
                NotifyOnAttack = TelegramNotifyOnAttack,
                NotifyOnPause = TelegramNotifyOnPause,
            });

            await _dialogService.MessageBox.Handle(new MessageBoxData("Information", "Settings saved."));
        }

        [ReactiveCommand]
        private async Task Import()
        {
            var path = await _dialogService.OpenFileDialog.Handle(Unit.Default);
            Dictionary<AccountSettingEnums, int> settings;
            try
            {
                var jsonString = await File.ReadAllTextAsync(path);
                settings = JsonSerializer.Deserialize<Dictionary<AccountSettingEnums, int>>(jsonString)!;
            }
            catch
            {
                await _dialogService.MessageBox.Handle(new MessageBoxData("Warning", "Invalid file."));
                return;
            }

            AccountSettingInput.Set(settings);
            var result = await _accountsettingInputValidator.ValidateAsync(AccountSettingInput);
            if (!result.IsValid)
            {
                await _dialogService.MessageBox.Handle(new MessageBoxData("Error", result.ToString()));
                return;
            }

            using var scope = _serviceScopeFactory.CreateScope(AccountId);
            var saveAccountSettingCommand = scope.ServiceProvider.GetRequiredService<SaveAccountSettingCommand.Handler>();
            await saveAccountSettingCommand.HandleAsync(new(AccountId, AccountSettingInput.Get()));

            await _dialogService.MessageBox.Handle(new MessageBoxData("Information", "Settings imported."));
        }

        [ReactiveCommand]
        private async Task Export()
        {
            var path = await _dialogService.SaveFileDialog.Handle(Unit.Default);
            if (string.IsNullOrEmpty(path)) return;

            using var scope = _serviceScopeFactory.CreateScope(AccountId);
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var settings = context.AccountsSetting
              .Where(x => x.AccountId == AccountId.Value)
              .ToDictionary(x => x.Setting, x => x.Value);

            var jsonString = JsonSerializer.Serialize(settings);
            await File.WriteAllTextAsync(path, jsonString);
            await _dialogService.MessageBox.Handle(new MessageBoxData("Information", "Settings exported."));
        }

        [ReactiveCommand]
        private Dictionary<AccountSettingEnums, int> LoadSettings(AccountId accountId)
        {
            using var scope = _serviceScopeFactory.CreateScope(AccountId);
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var settings = context.AccountsSetting
              .Where(x => x.AccountId == AccountId.Value)
              .ToDictionary(x => x.Setting, x => x.Value);
            return settings;
        }
    }
}