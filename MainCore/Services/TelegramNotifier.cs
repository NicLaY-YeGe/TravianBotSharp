using System.Net.Http.Json;
using System.Text.Json;

namespace MainCore.Services
{
    public sealed class TelegramAccountSetting
    {
        public string BotToken { get; set; } = "";
        public string ChatId { get; set; } = "";
        public bool NotifyOnAttack { get; set; } = true;
        public bool NotifyOnPause { get; set; } = true;
    }

    public interface ITelegramNotifier
    {
        TelegramAccountSetting Get(AccountId accountId);

        void Save(AccountId accountId, TelegramAccountSetting setting);

        Task NotifyAsync(AccountId accountId, string message, CancellationToken cancellationToken = default);
    }

    // Settings are kept in their own JSON file (next to TBS.db) instead of the SQLite settings
    // tables, because the bot token / chat id are strings and the existing AccountSetting table
    // only stores ints. Keeping this separate also means it never needs a DB schema change.
    [RegisterSingleton<ITelegramNotifier, TelegramNotifier>]
    public sealed class TelegramNotifier : ITelegramNotifier
    {
        private readonly ILogger _logger;
        private readonly HttpClient _httpClient = new();
        private readonly string _filePath = Path.Combine(AppContext.BaseDirectory, "telegram-settings.json");
        private readonly object _fileLock = new();
        private Dictionary<int, TelegramAccountSetting>? _cache;

        public TelegramNotifier(ILogger logger)
        {
            _logger = logger.ForContext<TelegramNotifier>();
        }

        private Dictionary<int, TelegramAccountSetting> Load()
        {
            lock (_fileLock)
            {
                if (_cache is not null) return _cache;

                try
                {
                    if (File.Exists(_filePath))
                    {
                        var json = File.ReadAllText(_filePath);
                        _cache = JsonSerializer.Deserialize<Dictionary<int, TelegramAccountSetting>>(json) ?? [];
                    }
                    else
                    {
                        _cache = [];
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Cannot read telegram-settings.json, starting with empty settings.");
                    _cache = [];
                }

                return _cache;
            }
        }

        private void Persist(Dictionary<int, TelegramAccountSetting> settings)
        {
            lock (_fileLock)
            {
                try
                {
                    var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(_filePath, json);
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Cannot save telegram-settings.json.");
                }
            }
        }

        public TelegramAccountSetting Get(AccountId accountId)
        {
            var settings = Load();
            return settings.TryGetValue(accountId.Value, out var setting) ? setting : new TelegramAccountSetting();
        }

        public void Save(AccountId accountId, TelegramAccountSetting setting)
        {
            var settings = Load();
            settings[accountId.Value] = setting;
            Persist(settings);
        }

        public async Task NotifyAsync(AccountId accountId, string message, CancellationToken cancellationToken = default)
        {
            var setting = Get(accountId);
            if (string.IsNullOrWhiteSpace(setting.BotToken) || string.IsNullOrWhiteSpace(setting.ChatId)) return;

            try
            {
                var url = $"https://api.telegram.org/bot{setting.BotToken}/sendMessage";
                var payload = new { chat_id = setting.ChatId, text = message };
                var response = await _httpClient.PostAsJsonAsync(url, payload, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.Warning("Telegram notification failed: {StatusCode} {Body}", response.StatusCode, body);
                }
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Telegram notification failed.");
            }
        }
    }
}
