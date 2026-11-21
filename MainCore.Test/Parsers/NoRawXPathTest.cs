using System.Text.RegularExpressions;

namespace MainCore.Test.Parsers
{
    // CLAUDE.md rule 2 / TODO P0 (2026-11-04): HtmlAgilityPack's HtmlNode.XPath is absolute and positional, so a
    // Playwright locator built from it matches nothing as soon as anything is inserted in front of the target
    // (live: SendResourceCommand, 30 s timeout). Use node.StableXPath() or an id/name/CSS selector instead.
    // This test reads the sources (CI checks the repository out), so the rule cannot be broken again unnoticed.
    public class NoRawXPathTest
    {
        // ".XPath" that is not a call (By.XPath(...) is the locator factory and is fine). ".StableXPath" does not match.
        private static readonly Regex RawNodeXPath = new(@"\.XPath\b(?!\s*\()", RegexOptions.Compiled);

        private static string? FindMainCoreDirectory()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null)
            {
                var candidate = Path.Combine(dir.FullName, "MainCore");
                if (File.Exists(Path.Combine(candidate, "MainCore.csproj"))) return candidate;
                dir = dir.Parent;
            }
            return null;
        }

        [Fact]
        public void MainCore_NeverUsesRawNodeXPath()
        {
            var mainCore = FindMainCoreDirectory();
            if (mainCore is null) return; // sources are not next to the test binaries (e.g. a packaged run)

            var offenders = new List<string>();
            foreach (var file in Directory.EnumerateFiles(mainCore, "*.cs", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(mainCore, file);
                if (relative.StartsWith("obj", StringComparison.OrdinalIgnoreCase) || relative.StartsWith("bin", StringComparison.OrdinalIgnoreCase)) continue;
                // HtmlNodeXPathExtensions builds StableXPath from node.XPath; BrowserTypes only names the locator kind in a text.
                if (Path.GetFileName(file) is "HtmlNodeXPathExtensions.cs" or "BrowserTypes.cs") continue;

                var lineNumber = 0;
                foreach (var line in File.ReadLines(file))
                {
                    lineNumber++;
                    var trimmed = line.TrimStart();
                    if (trimmed.StartsWith("//")) continue;
                    if (RawNodeXPath.IsMatch(line)) offenders.Add($"{relative}:{lineNumber}: {trimmed}");
                }
            }

            offenders.ShouldBeEmpty("raw HtmlNode.XPath found - use StableXPath() or a name/id selector:\n" + string.Join("\n", offenders));
        }
    }
}
