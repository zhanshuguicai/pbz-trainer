using PBZ.Core.Lock;
using PBZ.Core.Suite;
using PBZ.Core.Telemetry;

namespace PBZ.Core.Tests;

[TestClass]
public sealed class LockTests
{
    [TestMethod]
    public void Hotkey_ParsesAndFormatsCanonically()
    {
        var hk = Hotkey.Parse(" shift + ctrl+F2 ")!;
        Assert.AreEqual(HotkeyModifiers.Ctrl | HotkeyModifiers.Shift, hk.Modifiers);
        Assert.AreEqual("F2", hk.Key);
        Assert.AreEqual("Ctrl+Shift+F2", hk.ToString());
        Assert.AreEqual(hk, Hotkey.Parse(hk.ToString()));
    }

    [TestMethod]
    public void Hotkey_RejectsEmptyAndTwoMainKeys()
    {
        Assert.IsNull(Hotkey.Parse(""));
        Assert.IsNull(Hotkey.Parse("Ctrl+Alt"));
        Assert.IsNull(Hotkey.Parse("A+B"));
    }

    [TestMethod]
    public void Hotkey_DisplayUsesFriendlyNames()
    {
        Assert.AreEqual("Alt+1", Hotkey.Parse("Alt+D1")!.Display);
        Assert.AreEqual("小键盘5", Hotkey.Parse("NumPad5")!.Display);
        Assert.AreEqual("Ctrl+F1", Hotkey.Parse("Ctrl+F1")!.Display);
    }

    [TestMethod]
    public void Settings_DefaultsHaveDistinctHotkeys()
    {
        var s = LockSettings.Default;
        Assert.AreEqual(0, s.Duplicates().Count);
        foreach (var a in LockActions.All) Assert.IsNotNull(Hotkey.Parse(s.HotkeyText(a)), a);
    }

    [TestMethod]
    public void Settings_FlagsDuplicateHotkeys()
    {
        var s = LockSettings.Default.WithHotkey(LockActions.Spirit, "ctrl+f1");
        CollectionAssert.AreEqual(new[] { LockActions.Spirit }, s.Duplicates().ToArray());
    }

    [TestMethod]
    public void Settings_RoundTripAndCleanUnknownOrBadKeys()
    {
        var dir = Directory.CreateTempSubdirectory("pbz-lock-").FullName;
        var file = Path.Combine(dir, "lock.json");
        var saved = LockSettings.Default with { Shachi = false, Sound = false };
        saved.WithHotkey(LockActions.Health, "Alt+D2").Save(file);
        var read = LockSettings.Load(file);
        Assert.IsFalse(read.Shachi);
        Assert.IsFalse(read.Sound);
        Assert.AreEqual("Alt+D2", read.HotkeyText(LockActions.Health));

        File.WriteAllText(file, """{"health":true,"shachi":true,"ultimate":true,"spirit":true,"sound":true,"hotkeys":{"toggle":"Ctrl","bogus":"F9"}}""");
        read = LockSettings.Load(file);
        Assert.AreEqual("", read.HotkeyText(LockActions.Toggle));       // unusable text is cleared
        Assert.AreEqual("Ctrl+F3", read.HotkeyText(LockActions.Shachi)); // missing entries get defaults
        Assert.AreEqual("", read.HotkeyText("bogus"));
        Directory.Delete(dir, true);
    }

    [TestMethod]
    public void Config_LockDefaultsOffAndRoundTrips()
    {
        var dir = Directory.CreateTempSubdirectory("pbz-cfg-").FullName;
        File.WriteAllText(Path.Combine(dir, "config.json"), """{"revision":4,"compatible":true,"modules":{"parry":false,"items":false},"parryHotkey":"F7"}""");
        Assert.AreEqual(SuiteLock.Off, SuiteConfigStore.Read(dir).Lock);

        var wanted = new SuiteLock(true, true, false, true, false);
        SuiteConfigStore.Write(dir, SuiteConfigStore.Read(dir) with { Lock = wanted });
        var text = File.ReadAllText(Path.Combine(dir, "config.json"));
        StringAssert.Contains(text, "\"lock\"");
        StringAssert.Contains(text, "\"ultimate\": true");
        Assert.IsFalse(text.Contains("anyActive"), "computed property must not reach the game");
        var read = SuiteConfigStore.Read(dir);
        Assert.AreEqual(wanted, read.Lock);
        Assert.AreEqual(5, read.Revision);
        Directory.Delete(dir, true);
    }

    [TestMethod]
    public void Value_ParsingRules()
    {
        Assert.IsTrue(LockSettings.TryParseValue(LockActions.Shachi, " ", out var v, out _));
        Assert.IsNull(v); // empty = full
        Assert.IsTrue(LockSettings.TryParseValue(LockActions.Shachi, "300.25", out v, out _));
        Assert.AreEqual(300.3, v!.Value, 1e-9);
        Assert.IsTrue(LockSettings.TryParseValue(LockActions.Spirit, "2.6", out v, out _));
        Assert.AreEqual(3, v);
        Assert.IsTrue(LockSettings.TryParseValue(LockActions.Shachi, "0", out v, out _));
        Assert.AreEqual(0, v);
        Assert.IsFalse(LockSettings.TryParseValue(LockActions.Health, "0", out _, out _)); // would kill the character
        Assert.IsFalse(LockSettings.TryParseValue(LockActions.Ultimate, "-5", out _, out _));
        Assert.IsFalse(LockSettings.TryParseValue(LockActions.Ultimate, "abc", out _, out _));
    }

    [TestMethod]
    public void Value_RoundTripsAndDropsInvalidEntries()
    {
        var dir = Directory.CreateTempSubdirectory("pbz-lockv-").FullName;
        var file = Path.Combine(dir, "lock.json");
        LockSettings.Default.WithValue(LockActions.Shachi, 300).WithValue(LockActions.Spirit, 2).Save(file);
        var read = LockSettings.Load(file);
        Assert.AreEqual(300, read.Value(LockActions.Shachi));
        Assert.AreEqual(2, read.Value(LockActions.Spirit));
        Assert.IsNull(read.Value(LockActions.Health));
        Assert.IsNull(read.WithValue(LockActions.Shachi, null).Value(LockActions.Shachi));

        File.WriteAllText(file, """{"health":true,"shachi":true,"ultimate":true,"spirit":true,"sound":true,"values":{"health":0,"toggle":5,"ultimate":50}}""");
        read = LockSettings.Load(file);
        Assert.IsNull(read.Value(LockActions.Health)); // 0 health is refused
        Assert.IsNull(read.Value(LockActions.Toggle));
        Assert.AreEqual(50, read.Value(LockActions.Ultimate));
        Directory.Delete(dir, true);
    }

    [TestMethod]
    public void Config_CarriesValuesAndDefaultsToFull()
    {
        var dir = Directory.CreateTempSubdirectory("pbz-cfgv-").FullName;
        File.WriteAllText(Path.Combine(dir, "config.json"), """{"revision":1,"compatible":true,"lock":{"enabled":true,"health":true,"shachi":false,"ultimate":false,"spirit":false}}""");
        var read = SuiteConfigStore.Read(dir);
        Assert.AreEqual(SuiteLock.FullValue, read.Lock!.HealthValue);
        SuiteConfigStore.Write(dir, read with { Lock = read.Lock with { ShachiValue = 250 } });
        StringAssert.Contains(File.ReadAllText(Path.Combine(dir, "config.json")), "\"shachiValue\": 250");
        Assert.AreEqual(250, SuiteConfigStore.Read(dir).Lock!.ShachiValue);
        Directory.Delete(dir, true);
    }

    [TestMethod]
    public void Snapshot_ReadsLockModuleFlag()
    {
        const string line = """{"schemaVersion":1,"sessionId":"x","sequence":1,"monotonicMs":5,"lifecycle":"playing","modules":{"telemetry":true,"lock":true}}""";
        Assert.IsTrue(Snapshot.TryParse(line)!.Modules.Lock);
        Assert.IsFalse(Snapshot.TryParse(line.Replace("\"lock\":true", "\"lock\":false"))!.Modules.Lock);
    }

    [TestMethod]
    public void Snapshot_ReadsValuesAndToleratesOldLines()
    {
        const string line = """{"schemaVersion":1,"sessionId":"x","sequence":1,"monotonicMs":5,"lifecycle":"playing","values":{"shachi":{"current":321.6,"max":480,"valid":true},"ultimate":{"current":null,"max":null,"valid":false},"spirit":{"current":4,"max":5,"valid":true}}}""";
        var v = Snapshot.TryParse(line)!.Values!;
        Assert.AreEqual(321.6, v.Shachi.Current);
        Assert.IsFalse(v.Ultimate.Valid);
        Assert.AreEqual(5, v.Spirit.Max);
        var old = Snapshot.TryParse("""{"schemaVersion":1,"sessionId":"x","sequence":1,"monotonicMs":5,"lifecycle":"playing"}""")!.Values!;
        Assert.IsFalse(old.Shachi.Valid || old.Ultimate.Valid || old.Spirit.Valid);
    }
}
