using PBZ.Core.Lock;
using PBZ.Core.Suite;
using PBZ.Core.Telemetry;

namespace PBZ.Core.Tests;

[TestClass]
public sealed class ParryTests
{
    [TestMethod]
    public void Config_ParryRoundTripsAndDefaultsOn()
    {
        var dir = Directory.CreateTempSubdirectory("pbz-parry-").FullName;
        File.WriteAllText(Path.Combine(dir, "config.json"), """{"revision":2,"compatible":true,"modules":{"parry":false,"items":false}}""");
        var read = SuiteConfigStore.Read(dir);
        Assert.AreEqual(new SuiteParry(), read.Parry);
        Assert.IsFalse(read.Modules.Parry);
        SuiteConfigStore.Write(dir, read with { Modules = read.Modules with { Parry = true }, Parry = new SuiteParry(true, false, 90) });
        var text = File.ReadAllText(Path.Combine(dir, "config.json"));
        StringAssert.Contains(text, "\"leadMs\": 90");
        var again = SuiteConfigStore.Read(dir);
        Assert.IsTrue(again.Modules.Parry);
        Assert.IsFalse(again.Parry!.Dodge);
        Directory.Delete(dir, true);
    }

    [TestMethod]
    public void Settings_ParryHotkeyAndOptions()
    {
        var s = LockSettings.Default;
        Assert.AreEqual("F7", s.HotkeyText(LockActions.Parry));
        Assert.IsTrue(s.ParryDeflect && s.ParryDodge);
        var dir = Directory.CreateTempSubdirectory("pbz-parrys-").FullName;
        var file = Path.Combine(dir, "lock.json");
        File.WriteAllText(file, """{"health":true,"shachi":true,"ultimate":true,"spirit":true,"sound":true,"parryDodge":false,"parryLeadMs":999}""");
        var read = LockSettings.Load(file);
        Assert.IsFalse(read.ParryDodge);
        Assert.AreEqual(300, read.ParryLeadMs);
        Assert.AreEqual("F7", read.HotkeyText(LockActions.Parry)); // older file without the key gets the default
        Directory.Delete(dir, true);
    }

    [TestMethod]
    public void Snapshot_ReadsParryCounters()
    {
        const string line = """{"schemaVersion":1,"sessionId":"x","sequence":1,"monotonicMs":5,"lifecycle":"playing","modules":{"parry":true},"parry":{"deflects":7,"dodges":2,"hitsTaken":1,"clean":8}}""";
        var s = Snapshot.TryParse(line)!;
        Assert.IsTrue(s.Modules.Parry);
        Assert.AreEqual(new ParrySample(7, 2, 1, 8), s.Parry);
        Assert.IsNull(Snapshot.TryParse(line.Replace(",\"parry\":{\"deflects\":7,\"dodges\":2,\"hitsTaken\":1,\"clean\":8}", ""))!.Parry);
    }
}

[TestClass]
public sealed class ParryCommandTests
{
    [TestMethod]
    public void Parse_AcceptsOnlyCompleteLines()
    {
        var c = PBZ.Core.Combat.ParryCommand.TryParse("PBZPARRY 6aeb19bebc05 12 dodge end\n")!;
        Assert.AreEqual("6aeb19bebc05", c.Session);
        Assert.AreEqual(12, c.Seq);
        Assert.IsTrue(c.Dodge);
        Assert.IsFalse(PBZ.Core.Combat.ParryCommand.TryParse("PBZPARRY s 3 deflect end")!.Dodge);
        Assert.IsNull(PBZ.Core.Combat.ParryCommand.TryParse("PBZPARRY s 3 deflect")); // half written
        Assert.IsNull(PBZ.Core.Combat.ParryCommand.TryParse("PBZPARRY s x deflect end"));
        Assert.IsNull(PBZ.Core.Combat.ParryCommand.TryParse("PBZPARRY s 3 jump end"));
        Assert.IsNull(PBZ.Core.Combat.ParryCommand.TryParse(""));
    }

    [TestMethod]
    public void IsNew_BySessionAndSeq()
    {
        var a = new PBZ.Core.Combat.ParryCommand("s1", 5, false);
        Assert.IsTrue(PBZ.Core.Combat.ParryCommand.IsNew(null, a));
        Assert.IsFalse(PBZ.Core.Combat.ParryCommand.IsNew(a, a with { }));
        Assert.IsTrue(PBZ.Core.Combat.ParryCommand.IsNew(a, a with { Seq = 6 }));
        Assert.IsTrue(PBZ.Core.Combat.ParryCommand.IsNew(a, new("s2", 1, true))); // game restarted
    }

    [TestMethod]
    public void Keys_DefaultsMatchGameBindings()
    {
        Assert.AreEqual(2, PBZ.Core.Combat.PressKey.Find("RightMouse", "RightMouse").MouseButton);
        Assert.AreEqual(0x2A, PBZ.Core.Combat.PressKey.Find("LeftShift", "LeftShift").ScanCode);
        Assert.AreEqual("LeftShift", PBZ.Core.Combat.PressKey.Find("nope", "LeftShift").Id);
        Assert.AreEqual("RightMouse", PBZ.Core.Lock.LockSettings.Default.ParryDeflectKey);
    }
}
