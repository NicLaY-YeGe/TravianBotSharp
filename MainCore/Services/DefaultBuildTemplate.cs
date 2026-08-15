using System.Reflection;
using System.Text.Json;

namespace MainCore.Services
{
    // Loads the account-wide default build template used by ApplyBuildTemplateTask when a new
    // village is founded (see CLAUDE.md/PROJECT_CONTEXT.md §5l). Decided with the user
    // (2026-08-15): one fixed template for everyone, embedded in the app itself - not a
    // per-account file/UI import - so it's read once from an embedded resource (same mechanism
    // ChromeManager already uses for the browser extension files) rather than a user-editable
    // path.
    //
    // The embedded JSON is the exact List<JobDto> format the Build tab's own Export()/Import()
    // already reads/writes - the user's uploaded Task_goreve_gore_koy_gelisimi.tbs (63 entries:
    // 57 NormalBuild + 6 ResourceBuild) copied in as-is.
    public static class DefaultBuildTemplate
    {
        private static List<JobDto>? _cache;

        public static List<JobDto> Get()
        {
            if (_cache is not null) return _cache;

            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(x => x.EndsWith("DefaultBuildTemplate.json", StringComparison.Ordinal));

            if (resourceName is null)
            {
                _cache = [];
                return _cache;
            }

            using var stream = assembly.GetManifestResourceStream(resourceName)!;
            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();

            _cache = JsonSerializer.Deserialize<List<JobDto>>(json) ?? [];
            return _cache;
        }
    }
}
