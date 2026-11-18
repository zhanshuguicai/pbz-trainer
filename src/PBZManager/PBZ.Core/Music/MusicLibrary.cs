using System.Security.Cryptography;

namespace PBZ.Core.Music;

// The "music" folder next to the exe holds the player's songs. Rules store songs inside it as relative paths
// ("music\song.mp3"), so the whole program folder can be moved or shared without breaking them.
public static class MusicLibrary
{
    public const string FolderName = "music";

    public static string Folder(string exeDir) => Path.Combine(exeDir, FolderName);

    // The full path of a stored song; songs outside the folder are stored as full paths already.
    public static string Resolve(string exeDir, string track) =>
        Path.IsPathFullyQualified(track) ? track : Path.GetFullPath(Path.Combine(exeDir, track));

    // Copies a song into the music folder and returns the path to store. A song that cannot be found is matched by
    // file name against the folder; if that fails too, or the copy fails, the path is returned unchanged.
    public static string Import(string exeDir, string track)
    {
        if (string.IsNullOrWhiteSpace(track)) return track;
        var folder = Path.GetFullPath(Folder(exeDir));
        var source = Resolve(exeDir, track);
        if (source.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return Path.GetRelativePath(exeDir, source);
        var name = Path.GetFileName(source);
        if (!File.Exists(source))
            return File.Exists(Path.Combine(folder, name)) ? Path.Combine(FolderName, name) : track;
        try
        {
            Directory.CreateDirectory(folder);
            var target = FreeName(folder, source);
            if (!File.Exists(target)) File.Copy(source, target);
            return Path.Combine(FolderName, Path.GetFileName(target));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return track;
        }
    }

    // Moves every song of the rules into the music folder.
    public static MusicSettings Adopt(string exeDir, MusicSettings settings) => settings with
    {
        Rules = settings.Rules?.Select(r => r with
        {
            TrackPath = r.TrackPath is { } t ? Import(exeDir, t) : null,
            Tracks = r.Tracks?.Select(t => Import(exeDir, t)).ToList(),
        }).ToList(),
    };

    // Songs the rules use that are not on this computer, without repeats.
    public static IReadOnlyList<string> Missing(string exeDir, IEnumerable<PlayRule>? rules) =>
        (rules ?? []).SelectMany(r => r.AllTracks).Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(t => !File.Exists(Resolve(exeDir, t))).ToList();

    // The same file already in the folder is reused; a different file with that name gets " (2)", " (3)" and so on.
    private static string FreeName(string folder, string source)
    {
        var stem = Path.GetFileNameWithoutExtension(source);
        var ext = Path.GetExtension(source);
        var target = Path.Combine(folder, stem + ext);
        for (var i = 2; File.Exists(target) && !SameFile(source, target); i++)
            target = Path.Combine(folder, $"{stem} ({i}){ext}");
        return target;
    }

    private static bool SameFile(string a, string b)
    {
        if (new FileInfo(a).Length != new FileInfo(b).Length) return false;
        using var fa = File.OpenRead(a);
        using var fb = File.OpenRead(b);
        return SHA256.HashData(fa).AsSpan().SequenceEqual(SHA256.HashData(fb));
    }
}
