namespace PBZ.Core;

// Where the manager keeps its own files (settings, lock settings, install manifest, hash cache, event log).
// They live in a "data" folder next to the exe, not in %LOCALAPPDATA%: programs started from a packaged app
// (for example the Claude desktop app) get their AppData writes redirected into that package's private folder, so
// the same manager started from Explorer could not see them and treated its own install as foreign.
public static class DataLocation
{
    public static readonly string[] Files =
        ["settings.json", "lock.json", "pbzsuite-manifest.json", "exe-hash-cache.json", "events.log"];

    // Shipped in the "defaults" folder next to the exe; used only when the data folder has no settings yet.
    private static readonly string[] DefaultFiles = ["settings.json", "lock.json"];

    // Returns the data folder, creating it. If it holds none of the known files yet, fills it from the legacy
    // folders: each file is taken from the first legacy folder that has it (a manager started from Explorer and one
    // started from a packaged app may each have written some of them), and what is still absent from "defaults".
    // Returns the legacy folders copied from.
    public static (string Dir, IReadOnlyList<string> MigratedFrom) Prepare(string exeDir, IEnumerable<string> legacyDirs)
    {
        var dir = Path.Combine(exeDir, "data");
        Directory.CreateDirectory(dir);
        var used = new List<string>();
        if (Files.Any(f => File.Exists(Path.Combine(dir, f)))) return (dir, used);
        var legacy = legacyDirs.Where(Directory.Exists).ToList();
        foreach (var f in Files)
        {
            var src = legacy.Select(d => Path.Combine(d, f)).FirstOrDefault(File.Exists);
            if (src is null) continue;
            File.Copy(src, Path.Combine(dir, f));
            var from = Path.GetDirectoryName(src)!;
            if (!used.Contains(from)) used.Add(from);
        }
        foreach (var f in DefaultFiles)
        {
            var shipped = Path.Combine(exeDir, "defaults", f);
            var target = Path.Combine(dir, f);
            if (File.Exists(shipped) && !File.Exists(target)) File.Copy(shipped, target);
        }
        return (dir, used);
    }

    // Legacy locations, most likely first: this process's view of %LOCALAPPDATA%\PBZManager, then the redirected
    // copies inside packaged apps' private folders (AppData\Local\Packages\*\LocalCache\Local\PBZManager).
    public static IEnumerable<string> LegacyDirs()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        yield return Path.Combine(local, "PBZManager");
        var packages = Path.Combine(local, "Packages");
        if (!Directory.Exists(packages)) yield break;
        string[] candidates;
        try { candidates = Directory.GetDirectories(packages); }
        catch (UnauthorizedAccessException) { yield break; }
        foreach (var p in candidates.OrderByDescending(p => Directory.GetLastWriteTimeUtc(p)))
        {
            var d = Path.Combine(p, "LocalCache", "Local", "PBZManager");
            if (Directory.Exists(d)) yield return d;
        }
    }
}
