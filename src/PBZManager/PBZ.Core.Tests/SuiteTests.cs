using PBZ.Core.Suite;

namespace PBZ.Core.Tests;

[TestClass]
public sealed class SuiteTests
{
    private sealed class Sandbox : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("pbz-suite-").FullName;
        public string Payload => Path.Combine(Root, "payload");
        public string Target => Path.Combine(Root, "game", "Mods", "PBZSuite");
        public string Manifest => Path.Combine(Root, "data", "manifest.json");
        public string Archive => Path.Combine(Root, "archive");

        public Sandbox()
        {
            Write(Payload, "enabled.txt", "");
            Write(Payload, "Scripts/main.lua", "-- v1");
            Write(Payload, "runtime/README.txt", "readme");
        }

        public SuiteInstaller Installer() => new(Payload, Target, Manifest);

        public static void Write(string dir, string rel, string text)
        {
            var path = Path.Combine(dir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }

        public void Dispose() => Directory.Delete(Root, true);
    }

    [TestMethod]
    public void Install_Update_Uninstall_ArchivesRuntimeOutput()
    {
        using var box = new Sandbox();
        var installer = box.Installer();
        Assert.AreEqual(SuiteState.NotInstalled, installer.GetState());
        installer.Install();
        Assert.AreEqual(SuiteState.Installed, installer.GetState());

        Sandbox.Write(box.Target, "runtime/telemetry-6aea1449e87e-0.jsonl", "{}\n");
        Sandbox.Write(box.Target, "runtime/config.json", "{}");
        Assert.AreEqual(SuiteState.Installed, installer.GetState(), "runtime output does not change state");

        Sandbox.Write(box.Payload, "Scripts/main.lua", "-- v2");
        Assert.AreEqual(SuiteState.Outdated, installer.GetState());
        installer.Install();
        Assert.AreEqual("-- v2", File.ReadAllText(Path.Combine(box.Target, "Scripts/main.lua")));

        var archive = installer.Uninstall(box.Archive);
        Assert.IsFalse(Directory.Exists(box.Target));
        Assert.IsFalse(File.Exists(box.Manifest));
        Assert.IsTrue(File.Exists(Path.Combine(archive!, "runtime", "telemetry-6aea1449e87e-0.jsonl")));
    }

    [TestMethod]
    public void Uninstall_StopsOnUnknownOrModifiedFiles()
    {
        using var box = new Sandbox();
        var installer = box.Installer();
        installer.Install();
        Sandbox.Write(box.Target, "Scripts/other.lua", "x");
        Assert.ThrowsException<InvalidOperationException>(() => installer.Uninstall(box.Archive));
        File.Delete(Path.Combine(box.Target, "Scripts/other.lua"));

        Sandbox.Write(box.Target, "Scripts/main.lua", "-- edited");
        Assert.AreEqual(SuiteState.Modified, installer.GetState());
        Assert.ThrowsException<InvalidOperationException>(() => installer.Uninstall(box.Archive));
        Assert.IsTrue(Directory.Exists(box.Target), "nothing removed");
    }

    [TestMethod]
    public void Install_RefusesToOverwriteForeignFolder()
    {
        using var box = new Sandbox();
        Sandbox.Write(box.Target, "Scripts/main.lua", "-- someone else");
        var installer = box.Installer();
        Assert.AreEqual(SuiteState.Foreign, installer.GetState());
        Assert.ThrowsException<InvalidOperationException>(installer.Install);
        Assert.IsFalse(installer.TryAdopt(), "different content is not adopted");
    }

    [TestMethod]
    public void TryAdopt_AcceptsScriptInstallWithRuntimeOutput()
    {
        using var box = new Sandbox();
        foreach (var rel in new[] { "enabled.txt", "Scripts/main.lua", "runtime/README.txt" })
            Sandbox.Write(box.Target, rel, File.ReadAllText(Path.Combine(box.Payload, rel)));
        Sandbox.Write(box.Target, "runtime/config.json", "{}");
        var installer = box.Installer();
        Assert.IsTrue(installer.TryAdopt());
        Assert.AreEqual(SuiteState.Installed, installer.GetState());
    }

    [TestMethod]
    public void Reinstall_SameVersion_IsHarmless()
    {
        using var box = new Sandbox();
        var installer = box.Installer();
        installer.Install();
        installer.Install();
        Assert.AreEqual(SuiteState.Installed, installer.GetState());
        Assert.AreEqual(0, Directory.GetFiles(box.Target, "*.pbznew", SearchOption.AllDirectories).Length);
    }

    [TestMethod]
    public void Update_KeepsRuntimeOutputOfAllModules_AndUninstallArchivesIt()
    {
        using var box = new Sandbox();
        var installer = box.Installer();
        installer.Install();
        foreach (var f in new[] { "runtime/inventory.json", "runtime/inventory.json.tmp", "runtime/parry-cmd.txt", "runtime/config.json" })
            Sandbox.Write(box.Target, f, "x");
        Sandbox.Write(box.Payload, "Scripts/main.lua", "-- v2");
        installer.Install();
        Assert.IsTrue(File.Exists(Path.Combine(box.Target, "runtime", "parry-cmd.txt")), "update keeps runtime output");
        var archive = installer.Uninstall(box.Archive)!;
        Assert.IsTrue(File.Exists(Path.Combine(archive, "runtime", "inventory.json")));
        Assert.IsFalse(Directory.Exists(box.Target));
    }

    [TestMethod]
    public void Update_FailingHalfway_LeavesInstalledFilesUntouched()
    {
        using var box = new Sandbox();
        var installer = box.Installer();
        installer.Install();
        Sandbox.Write(box.Payload, "Scripts/main.lua", "-- v2");
        Sandbox.Write(box.Payload, "zz_last.txt", "new file");
        // Lock the last payload file so the copy fails after main.lua was already staged.
        using (new FileStream(Path.Combine(box.Payload, "zz_last.txt"), FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.ThrowsException<IOException>(() => installer.Install());
        Assert.AreEqual("-- v1", File.ReadAllText(Path.Combine(box.Target, "Scripts/main.lua")));
        Assert.AreEqual(0, Directory.GetFiles(box.Target, "*.pbznew", SearchOption.AllDirectories).Length, "temp files cleaned up");
        Assert.AreEqual(SuiteState.Outdated, installer.GetState(), "still a normal, updatable install");
        installer.Install();
        Assert.AreEqual(SuiteState.Installed, installer.GetState());
    }

    [TestMethod]
    public void Config_WritesIncreasingRevisions()
    {
        using var box = new Sandbox();
        var dir = Path.Combine(box.Root, "runtime");
        var a = SuiteConfigStore.Write(dir, SuiteConfig.Default);
        var b = SuiteConfigStore.Write(dir, SuiteConfig.Default with { Compatible = true });
        Assert.AreEqual(1, a.Revision);
        Assert.AreEqual(2, b.Revision);
        var read = SuiteConfigStore.Read(dir);
        Assert.IsTrue(read.Compatible);
        StringAssert.Contains(File.ReadAllText(Path.Combine(dir, "config.json")), "\"revision\": 2");
        Assert.IsFalse(File.Exists(Path.Combine(dir, "config.json.tmp")));
    }

    [TestMethod]
    public void Config_ReadFillsMissingFieldsFromOlderFiles()
    {
        using var box = new Sandbox();
        var dir = Path.Combine(box.Root, "runtime");
        Sandbox.Write(dir, "config.json", """{"revision":3,"compatible":false,"modules":{"parry":false,"items":false}}""");
        var read = SuiteConfigStore.Read(dir);
        Assert.AreEqual(3, read.Revision);
        Assert.AreEqual("F7", read.ParryHotkey);
        Sandbox.Write(dir, "config.json", """{"revision":5}""");
        Assert.IsNotNull(SuiteConfigStore.Read(dir).Modules);
    }
}
