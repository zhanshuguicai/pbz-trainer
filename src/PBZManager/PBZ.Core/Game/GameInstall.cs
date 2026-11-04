using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PBZ.Core.Game;

public sealed record GameInstall(string Root, string ExePath, string? BuildId)
{
    public const string AppId = "4115450";

    public string BinDir => Path.GetDirectoryName(ExePath)!;
    public string Ue4ssDir => Path.Combine(BinDir, "ue4ss");
    public string SuiteDir => Path.Combine(Ue4ssDir, "Mods", "PBZSuite");
    public string RuntimeDir => Path.Combine(SuiteDir, "runtime");
    public bool LoaderInstalled => File.Exists(Path.Combine(Ue4ssDir, "UE4SS.dll")) && File.Exists(Path.Combine(BinDir, "dwmapi.dll"));

    // Looks through the Steam libraries listed in libraryfolders.vdf for the game's app manifest.
    public static GameInstall? Locate(string? steamPath)
    {
        var candidates = new List<string>();
        foreach (var steam in new[] { steamPath, @"C:\Program Files (x86)\Steam" })
        {
            if (string.IsNullOrWhiteSpace(steam)) continue;
            candidates.Add(steam);
            var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf))
                foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                    candidates.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
        }
        foreach (var library in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var acf = Path.Combine(library, "steamapps", $"appmanifest_{AppId}.acf");
            if (!File.Exists(acf)) continue;
            var text = File.ReadAllText(acf);
            var installDir = Field(text, "installdir") ?? "PBZ";
            var root = Path.Combine(library, "steamapps", "common", installDir);
            var exe = Path.Combine(root, "platform", "steam", "PBZ.exe");
            if (File.Exists(exe)) return new GameInstall(root, exe, Field(text, "buildid"));
        }
        return null;
    }

    private static string? Field(string acf, string name)
    {
        var m = Regex.Match(acf, $"\"{name}\"\\s+\"([^\"]*)\"");
        return m.Success ? m.Groups[1].Value : null;
    }
}

// The game builds PBZSuite has been verified against (docs/p2-field-sources.md, evidence/t0-baseline-20261103.json).
public static class GameCompatibility
{
    public sealed record KnownBuild(string BuildId, string ExeSha256, long ExeBytes);

    public static readonly IReadOnlyList<KnownBuild> Verified =
    [
        new("26266538", "7B4FCD286A350780FAF0824D73AA150B21CBF1BCCDE883E6A96386CBE09973B4", 511072344),
        // Steam update 2026-11-04 03:29. Verified on it: UE4SS + PBZSuite 0.3.0 load, telemetry, item edit
        // (Weapon_Material_08 4->10), deflect frames (evidence/t5b/round1-UE4SS.log). Value lock not re-tested yet.
        new("26367565", "4415933CE33476F14C837ABB61A1B60AF8B78CD81783896501913A7AAC69E423", 498913272),
    ];

    public sealed record Result(bool Compatible, string ExeSha256, string Reason);

    // Hashing the 500 MB EXE takes a few seconds, so results are cached by size and write time.
    public static async Task<Result> CheckAsync(GameInstall game, string cacheFile, CancellationToken ct = default)
    {
        var info = new FileInfo(game.ExePath);
        var stamp = $"{info.Length}:{info.LastWriteTimeUtc.Ticks}";
        string? sha = null;
        try
        {
            if (File.Exists(cacheFile))
            {
                var cached = JsonSerializer.Deserialize<Dictionary<string, string>>(await File.ReadAllTextAsync(cacheFile, ct));
                if (cached is not null && cached.TryGetValue("stamp", out var s) && s == stamp) cached.TryGetValue("sha256", out sha);
            }
        }
        catch (JsonException) { }
        if (sha is null)
        {
            await using var fs = new FileStream(game.ExePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 20, useAsync: true);
            sha = Convert.ToHexString(await SHA256.HashDataAsync(fs, ct));
            Directory.CreateDirectory(Path.GetDirectoryName(cacheFile)!);
            await File.WriteAllTextAsync(cacheFile, JsonSerializer.Serialize(new Dictionary<string, string> { ["stamp"] = stamp, ["sha256"] = sha }), ct);
        }
        var match = Verified.FirstOrDefault(b => b.ExeSha256.Equals(sha, StringComparison.OrdinalIgnoreCase));
        if (match is null) return new Result(false, sha, "游戏 EXE 与已验证版本不同（可能已更新），写入类功能保持关闭");
        if (game.BuildId is not null && game.BuildId != match.BuildId)
            return new Result(false, sha, $"Steam 版本号 {game.BuildId} 与已验证的 {match.BuildId} 不符");
        return new Result(true, sha, $"已验证版本 {match.BuildId}");
    }
}
