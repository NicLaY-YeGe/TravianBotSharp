namespace MainCore.Services
{
    // 2026-11-18 (live log ts12.x1.europe 2026-11-17 16:44-17:01, account paused): Travian moved to clean URLs on newer
    // servers - dorf1.php is now /resources, dorf2.php is /village, build.php is /building (proven by the page capture
    // Europe_12.html: navigation hrefs and data-village-perspective="perspectiveResources"). Older servers (e.g. ts50.x5)
    // may still serve the .php names. Every "which page am I on" decision goes through this class so both schemes work and
    // a future rename is a one-line change. Only the path is looked at - query strings (newdid=, id=) never matter.
    public static class TravianUrl
    {
        // Resource fields (village overview, "dorf1").
        public static bool IsDorf1(string? url) => PathEndsWith(url, "dorf1.php", "/resources");

        // Village centre ("dorf2"). "/village/statistics" is a different page and does NOT match.
        public static bool IsDorf2(string? url) => PathEndsWith(url, "dorf2.php", "/village");

        // Either overview page - what a successful login or a finished upgrade lands on.
        public static bool IsDorf(string? url) => IsDorf1(url) || IsDorf2(url);

        // A building / resource-field page (build.php?id=X on old servers). "/slot" is the new scheme's link for a field
        // or an empty slot; it is accepted as well, the exact target is only confirmed for /building.
        public static bool IsBuilding(string? url) => PathEndsWith(url, "build.php", "/building", "/slot");

        private static bool PathEndsWith(string? url, params string[] suffixes)
        {
            var path = GetPath(url);
            if (path.Length == 0) return false;
            foreach (var suffix in suffixes)
            {
                if (path.EndsWith(suffix, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private static string GetPath(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return "";

            string path;
            // "://" check: on Linux a bare "/resources" would otherwise parse as an absolute file:// URI.
            if (url.Contains("://") && Uri.TryCreate(url, UriKind.Absolute, out var uri)) path = uri.AbsolutePath;
            else
            {
                path = url;
                var cut = path.IndexOfAny(['?', '#']);
                if (cut >= 0) path = path[..cut];
            }

            path = path.ToLowerInvariant().TrimEnd('/');
            return path;
        }
    }
}
