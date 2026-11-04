namespace PBZ.Core.Tests;

[TestClass]
public sealed class DataLocationTests
{
    [TestMethod]
    public void Prepare_TakesEachFileFromFirstLegacyFolderThatHasIt()
    {
        var root = Directory.CreateTempSubdirectory("pbz-data-").FullName;
        var exe = Path.Combine(root, "bin");
        var real = Path.Combine(root, "real");
        var packaged = Path.Combine(root, "packaged");
        Directory.CreateDirectory(real);
        Directory.CreateDirectory(packaged);
        File.WriteAllText(Path.Combine(real, "settings.json"), "real-settings");
        File.WriteAllText(Path.Combine(packaged, "settings.json"), "packaged-settings");
        File.WriteAllText(Path.Combine(packaged, "pbzsuite-manifest.json"), "manifest");

        var (dir, from) = DataLocation.Prepare(exe, [real, Path.Combine(root, "missing"), packaged]);

        Assert.AreEqual(Path.Combine(exe, "data"), dir);
        Assert.AreEqual("real-settings", File.ReadAllText(Path.Combine(dir, "settings.json")));
        Assert.AreEqual("manifest", File.ReadAllText(Path.Combine(dir, "pbzsuite-manifest.json")));
        CollectionAssert.AreEqual(new[] { real, packaged }, from.ToArray());

        // Second start: the data folder is in use, nothing is copied again.
        File.WriteAllText(Path.Combine(real, "lock.json"), "late");
        var (_, again) = DataLocation.Prepare(exe, [real, packaged]);
        Assert.AreEqual(0, again.Count);
        Assert.IsFalse(File.Exists(Path.Combine(dir, "lock.json")));
        Directory.Delete(root, true);
    }
}
