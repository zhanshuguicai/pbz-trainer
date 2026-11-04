using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PBZ.Core.Suite;

public sealed record ManifestFile(string RelativePath, string Sha256, long Bytes);

public sealed record SuiteManifest(string Product, string Destination, string Status, List<ManifestFile> Files, DateTimeOffset InstalledAt);

public enum SuiteState
{
    NotInstalled,
    Installed,       // matches the shipped payload
    Outdated,        // ours, but the payload has changed files
    Modified,        // ours, but files on disk no longer match the manifest
    Foreign,         // a PBZSuite folder exists that we cannot account for
}

// Same rules as tools/pbzsuite.ps1: never overwrite files we do not own, only remove what the manifest lists,
// and keep runtime output (telemetry, config) by archiving it before removal.
public sealed class SuiteInstaller
{
    private static readonly Regex RuntimeFile = new(@"^runtime[\\/](telemetry-[0-9a-f]+-\d+\.jsonl|config\.json|config\.json\.tmp|results\.jsonl|inventory\.json|inventory\.json\.tmp|parry-cmd\.txt)$", RegexOptions.IgnoreCase);
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    private readonly string _payloadDir;
    private readonly string _targetDir;
    private readonly string _manifestPath;

    public SuiteInstaller(string payloadDir, string targetDir, string manifestPath)
    {
        _payloadDir = payloadDir;
        _targetDir = targetDir;
        _manifestPath = manifestPath;
    }

    public SuiteState GetState()
    {
        var manifest = LoadManifest();
        if (manifest is null) return Directory.Exists(_targetDir) ? SuiteState.Foreign : SuiteState.NotInstalled;
        if (!manifest.Files.All(MatchesOnDisk)) return SuiteState.Modified;
        var payload = Scan(_payloadDir);
        var same = payload.Count == manifest.Files.Count &&
                   payload.All(p => manifest.Files.Any(m => m.RelativePath.Equals(p.RelativePath, StringComparison.OrdinalIgnoreCase) && m.Sha256 == p.Sha256));
        return same ? SuiteState.Installed : SuiteState.Outdated;
    }

    // A folder installed by tools/pbzsuite.ps1 has no manager manifest. It is adopted only if it holds exactly the
    // payload plus runtime output.
    public bool TryAdopt()
    {
        if (LoadManifest() is not null || !Directory.Exists(_targetDir)) return false;
        var payload = Scan(_payloadDir);
        if (!payload.All(MatchesOnDisk)) return false;
        var extra = Directory.GetFiles(_targetDir, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(_targetDir, f))
            .Where(rel => !payload.Any(p => p.RelativePath.Equals(rel, StringComparison.OrdinalIgnoreCase)));
        if (!extra.All(rel => RuntimeFile.IsMatch(rel))) return false;
        SaveManifest(new SuiteManifest("PBZSuite", _targetDir, "INSTALLED", payload, DateTimeOffset.Now));
        return true;
    }

    public void Install()
    {
        var state = GetState();
        if (state is SuiteState.Foreign or SuiteState.Modified) throw new InvalidOperationException($"PBZSuite 目录状态为 {state}，请先人工检查");
        var payload = Scan(_payloadDir);
        var old = LoadManifest()?.Files ?? [];
        foreach (var file in payload)
        {
            var target = Path.Combine(_targetDir, file.RelativePath);
            var owned = old.Any(o => o.RelativePath.Equals(file.RelativePath, StringComparison.OrdinalIgnoreCase));
            if (File.Exists(target) && !owned) throw new InvalidOperationException($"目标文件已存在且不属于本工具：{file.RelativePath}");
        }
        // Two phases so a failure halfway leaves the installed files as they were: first copy every file next to its
        // target as *.pbznew and verify it, then swap them in. Temp files are removed if the first phase fails.
        var staged = new List<(string Temp, string Target)>();
        try
        {
            foreach (var file in payload)
            {
                var target = Path.Combine(_targetDir, file.RelativePath);
                var temp = target + ".pbznew";
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                staged.Add((temp, target));
                File.Copy(Path.Combine(_payloadDir, file.RelativePath), temp, overwrite: true);
                if (Hash(temp) != file.Sha256) throw new IOException($"复制后校验失败：{file.RelativePath}");
            }
        }
        catch
        {
            foreach (var (temp, _) in staged) TryDelete(temp);
            throw;
        }
        foreach (var (temp, target) in staged) File.Move(temp, target, overwrite: true);
        foreach (var gone in old.Where(o => !payload.Any(p => p.RelativePath.Equals(o.RelativePath, StringComparison.OrdinalIgnoreCase))))
            File.Delete(Path.Combine(_targetDir, gone.RelativePath));
        SaveManifest(new SuiteManifest("PBZSuite", _targetDir, "INSTALLED", payload, DateTimeOffset.Now));
    }

    // Returns the folder runtime output was archived to, or null if there was none.
    public string? Uninstall(string archiveRoot)
    {
        var manifest = LoadManifest() ?? throw new InvalidOperationException("没有安装清单，不能自动卸载");
        var bad = manifest.Files.FirstOrDefault(f => !MatchesOnDisk(f));
        if (bad is not null) throw new InvalidOperationException($"文件已被修改，停止卸载：{bad.RelativePath}");
        var owned = manifest.Files.Select(f => f.RelativePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var extra = Directory.GetFiles(_targetDir, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(_targetDir, f)).Where(rel => !owned.Contains(rel)).ToList();
        var unknown = extra.FirstOrDefault(rel => !RuntimeFile.IsMatch(rel));
        if (unknown is not null) throw new InvalidOperationException($"发现未知文件，停止卸载：{unknown}");

        string? archive = null;
        if (extra.Count > 0)
        {
            archive = Path.Combine(archiveRoot, "pbzsuite-runtime-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            foreach (var rel in extra)
            {
                var dest = Path.Combine(archive, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(Path.Combine(_targetDir, rel), dest);
            }
        }
        foreach (var rel in owned.Concat(extra)) File.Delete(Path.Combine(_targetDir, rel));
        foreach (var dir in Directory.GetDirectories(_targetDir, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
            Directory.Delete(dir, recursive: false);
        Directory.Delete(_targetDir, recursive: false);
        File.Delete(_manifestPath);
        return archive;
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private bool MatchesOnDisk(ManifestFile f)
    {
        var path = Path.Combine(_targetDir, f.RelativePath);
        return File.Exists(path) && Hash(path) == f.Sha256;
    }

    private SuiteManifest? LoadManifest()
    {
        if (!File.Exists(_manifestPath)) return null;
        return JsonSerializer.Deserialize<SuiteManifest>(File.ReadAllText(_manifestPath), Json);
    }

    private void SaveManifest(SuiteManifest manifest)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_manifestPath)!);
        File.WriteAllText(_manifestPath, JsonSerializer.Serialize(manifest, Json));
    }

    // The runtime folder ships only a README; generated files there are never part of the payload hash set.
    private static List<ManifestFile> Scan(string dir) =>
        Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
            .Select(f => new ManifestFile(Path.GetRelativePath(dir, f), Hash(f), new FileInfo(f).Length))
            .Where(f => !RuntimeFile.IsMatch(f.RelativePath))
            .OrderBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string Hash(string path)
    {
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(fs));
    }
}
