using PBZ.Core.Lock;
using PBZ.Core.Music;

namespace PBZ.Core.Tests;

[TestClass]
public sealed class MusicLibraryTests
{
    private string _root = "";
    private string _exe = "";

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "pbz-music-" + Guid.NewGuid().ToString("N"));
        _exe = Path.Combine(_root, "app");
        Directory.CreateDirectory(_exe);
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_root, true);

    private string Song(string name, string content)
    {
        var dir = Path.Combine(_root, "downloads");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    [TestMethod]
    public void Import_CopiesIntoFolderAndStoresRelativePath()
    {
        var stored = MusicLibrary.Import(_exe, Song("歌 A.mp3", "aaa"));
        Assert.AreEqual(Path.Combine("music", "歌 A.mp3"), stored);
        Assert.AreEqual("aaa", File.ReadAllText(MusicLibrary.Resolve(_exe, stored)));
        Assert.AreEqual(stored, MusicLibrary.Import(_exe, stored), "a song already in the folder stays as it is");
    }

    [TestMethod]
    public void Import_ReusesSameFileAndKeepsDifferentFileWithSameName()
    {
        var first = MusicLibrary.Import(_exe, Song("a.mp3", "one"));
        Assert.AreEqual(first, MusicLibrary.Import(_exe, Song("a.mp3", "one")));
        var other = MusicLibrary.Import(_exe, Song("a.mp3", "two"));
        Assert.AreEqual(Path.Combine("music", "a (2).mp3"), other);
        Assert.AreEqual("one", File.ReadAllText(MusicLibrary.Resolve(_exe, first)), "the first song is not overwritten");
    }

    [TestMethod]
    public void Import_MissingSongMatchesByNameOrStaysUnchanged()
    {
        var gone = Path.Combine(_root, "gone", "b.mp3");
        Assert.AreEqual(gone, MusicLibrary.Import(_exe, gone));
        Directory.CreateDirectory(MusicLibrary.Folder(_exe));
        File.WriteAllText(Path.Combine(MusicLibrary.Folder(_exe), "b.mp3"), "b");
        Assert.AreEqual(Path.Combine("music", "b.mp3"), MusicLibrary.Import(_exe, gone));
    }

    [TestMethod]
    public void Adopt_RewritesRulesAndMissingListsWhatIsAbsent()
    {
        var song = Song("c.mp3", "c");
        var rule = MusicSettings.DefaultRules(song, Path.Combine(_root, "gone", "d.mp3"));
        var adopted = MusicLibrary.Adopt(_exe, MusicSettings.Default with { Rules = rule });
        Assert.AreEqual(Path.Combine("music", "c.mp3"), adopted.Rules![0].TrackPath);
        var missing = MusicLibrary.Missing(_exe, adopted.Rules);
        Assert.AreEqual(1, missing.Count);
        StringAssert.EndsWith(missing[0], "d.mp3");
    }

    [TestMethod]
    public void Prepare_FillsFirstRunFromDefaultsButKeepsExistingData()
    {
        var defaults = Path.Combine(_exe, "defaults");
        Directory.CreateDirectory(defaults);
        File.WriteAllText(Path.Combine(defaults, "settings.json"), "default");
        File.WriteAllText(Path.Combine(defaults, "lock.json"), "default");
        var (dir, _) = DataLocation.Prepare(_exe, []);
        Assert.AreEqual("default", File.ReadAllText(Path.Combine(dir, "settings.json")));

        File.WriteAllText(Path.Combine(dir, "settings.json"), "mine");
        File.Delete(Path.Combine(dir, "lock.json"));
        DataLocation.Prepare(_exe, []);
        Assert.AreEqual("mine", File.ReadAllText(Path.Combine(dir, "settings.json")), "an update never replaces the user's settings");
    }

    // The files that go into the public package: they must load, and must not carry paths from this computer.
    [TestMethod]
    public void ShippedDefaults_LoadAndUseOnlyTheMusicFolder()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "PBZManager", "defaults"))) dir = dir.Parent;
        Assert.IsNotNull(dir, "defaults folder not found");
        var defaults = Path.Combine(dir.FullName, "PBZManager", "defaults");

        var music = MusicSettings.Load(Path.Combine(defaults, "settings.json"));
        Assert.AreEqual(4, music.Rules!.Count);
        foreach (var track in music.Rules.SelectMany(r => r.AllTracks))
            StringAssert.StartsWith(track, "music" + Path.DirectorySeparatorChar);

        var lockSettings = LockSettings.Load(Path.Combine(defaults, "lock.json"));
        Assert.AreEqual(100, lockSettings.ParryLeadMs);
        Assert.AreEqual("F7", lockSettings.HotkeyText(LockActions.Parry));
        Assert.AreEqual("Ctrl+F9", lockSettings.HotkeyText(LockActions.SavePos), "hotkeys added later still get their defaults");
    }
}
