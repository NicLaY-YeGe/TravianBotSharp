using MainCore.UI.Models.Output;
using MainCore.UI.ViewModels.Abstract;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;
using System.Net.Http;

namespace MainCore.UI.ViewModels.Tabs
{
    // Given a target coordinate, lists nearby villages sorted by distance - name, coordinate,
    // player, tribe, alliance and population for each. Reads the server's public map.sql
    // export (see MapSqlParser.cs for the verified column layout and format notes), not the
    // logged-in game session, so this works even while the bot's browser is doing something
    // else (2026-08-21, user request).
    [RegisterSingleton<MapAnalysisViewModel>]
    public partial class MapAnalysisViewModel : AccountTabViewModelBase
    {
        private readonly IDialogService _dialogService;
        private readonly ICustomServiceScopeFactory _serviceScopeFactory;

        // A plain HttpClient, not the bot's Selenium browser - map.sql is a static file with
        // no login/JS required (same approach TelegramNotifier.cs uses for its own HTTP
        // calls). Kept as one shared instance for the lifetime of the singleton ViewModel
        // rather than a new HttpClient per search.
        private static readonly HttpClient _httpClient = new();

        public ObservableCollection<string> Results { get; } = new();

        [Reactive]
        private string _targetX = "";

        [Reactive]
        private string _targetY = "";

        [Reactive]
        private string _maxResults = "50";

        [Reactive]
        private string _statusText = "";

        // Backs CanSearch below - disables the Search button while a search is already in
        // flight, so the button isn't clickable again mid-download and (more importantly, see
        // 2026-08-23 user report) so there's some visible sign that something is happening
        // instead of the UI looking unchanged while map.sql downloads.
        [ObservableAsProperty]
        private bool _canSearch;

        public MapAnalysisViewModel(IDialogService dialogService, ICustomServiceScopeFactory serviceScopeFactory)
        {
            _dialogService = dialogService;
            _serviceScopeFactory = serviceScopeFactory;

            _canSearchHelper = SearchCommand.IsExecuting.Select(x => !x).ToProperty(this, vm => vm.CanSearch);

            SearchCommand.Subscribe(rows =>
            {
                Results.Clear();
                foreach (var row in rows) Results.Add(row);
                StatusText = rows.Count == 0 ? "" : $"{rows.Count} sonuç bulundu.";
            });
        }

        protected override async Task Load(AccountId accountId)
        {
            // Intentionally does nothing to Results: this used to clear the list on every tab
            // activation, which wiped out a completed search's results if the user switched
            // away and back before checking them (2026-08-23 user report - Search keeps
            // running in the background regardless of which tab is visible, since nothing
            // cancels it on deactivation, so results could finish while the user was on a
            // different tab). Results are now only cleared when a new search actually starts,
            // in SearchCommand's Subscribe above - so a finished search's results persist
            // across tab switches until the user searches again.
            await Task.CompletedTask;
        }

        [ReactiveCommand]
        private async Task<List<string>> Search()
        {
            StatusText = "Aranıyor...";

            if (!int.TryParse(TargetX, out var targetX) || !int.TryParse(TargetY, out var targetY))
            {
                StatusText = "";
                await _dialogService.MessageBox.Handle(new MessageBoxData("Error", "Enter valid X/Y target coordinates."));
                return [];
            }

            if (!int.TryParse(MaxResults, out var maxResults) || maxResults < 1)
            {
                maxResults = 50;
            }
            // Cap regardless of what was typed - map.sql can hold several thousand villages,
            // and dumping all of them into the results list would just make it unusable.
            maxResults = Math.Min(maxResults, 500);

            string server;
            using (var scope = _serviceScopeFactory.CreateScope(AccountId))
            {
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var account = context.Accounts.FirstOrDefault(x => x.Id == AccountId.Value);
                if (account is null || string.IsNullOrWhiteSpace(account.Server))
                {
                    StatusText = "";
                    await _dialogService.MessageBox.Handle(new MessageBoxData("Error", "Could not find this account's server address."));
                    return [];
                }
                server = account.Server;
            }

            string content;
            try
            {
                var url = $"{server.TrimEnd('/')}/map.sql";
                content = await _httpClient.GetStringAsync(url);
            }
            catch (Exception ex)
            {
                StatusText = "";
                await _dialogService.MessageBox.Handle(new MessageBoxData("Error", $"Could not download map.sql from the server: {ex.Message}"));
                return [];
            }

            var villages = MapSqlParser.Parse(content);
            if (villages.Count == 0)
            {
                StatusText = "";
                await _dialogService.MessageBox.Handle(new MessageBoxData("Warning", "map.sql was downloaded but no villages could be parsed from it - this server may not publish the map in the expected format."));
                return [];
            }

            // Travian's own in-game "distance" is the plain Euclidean distance between the
            // two coordinates - no special hex/grid adjustment.
            return villages
                .Select(v => (Distance: Math.Sqrt(Math.Pow(v.X - targetX, 2) + Math.Pow(v.Y - targetY, 2)), Village: v))
                .OrderBy(r => r.Distance)
                .Take(maxResults)
                .Select(r => FormatRow(r.Distance, r.Village))
                .ToList();
        }

        private static string FormatRow(double distance, MapVillage v)
        {
            var allianceText = string.IsNullOrEmpty(v.AllianceTag) ? "no alliance" : v.AllianceTag;
            var capitalText = v.IsCapital ? ", capital" : "";
            var tribeText = Enum.IsDefined(typeof(TribeEnums), v.Tid) ? ((TribeEnums)v.Tid).ToString() : $"Tribe {v.Tid}";

            return $"{distance:0.00} | ({v.X}|{v.Y}) {v.VillageName} - {v.PlayerName} ({tribeText}) [{allianceText}] - Pop {v.Population}{capitalText}";
        }
    }
}
