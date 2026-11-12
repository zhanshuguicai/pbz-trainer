using PBZ.Core.Lock;
using PBZ.Core.Player;
using PBZ.Core.Suite;
using PBZ.Core.Telemetry;

namespace PBZ.Core.Tests;

[TestClass]
public sealed class PlayerToolsTests
{
    [TestMethod]
    public void Cheats_ClampAndNeutral()
    {
        Assert.IsFalse(SuiteCheats.Neutral.AnyActive);
        var c = new SuiteCheats(GameSpeed: 50, MoveSpeed: 0, Damage: 1000, Taken: -1, Medicine: true).Clamped();
        Assert.AreEqual(5, c.GameSpeed);
        Assert.AreEqual(0.5, c.MoveSpeed);
        Assert.AreEqual(100, c.Damage);
        Assert.AreEqual(0, c.Taken);
        Assert.IsTrue(c.AnyActive);
    }

    // Lua reads cheats.gameSpeed / moveSpeed / damage / taken / medicine / fly and playerOps[].seq / kind / x / y / z / value.
    [TestMethod]
    public void Config_UsesTheNamesTheGameReads()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pbz-player-" + Guid.NewGuid().ToString("N"));
        try
        {
            var config = PlayerEdits.Append(SuiteConfig.Default with { Cheats = new SuiteCheats(2, 1.5, 10, 0, true, true) },
                new PlayerOp(0, "teleport", 1, 2, 3));
            SuiteConfigStore.Write(dir, config);
            var text = File.ReadAllText(Path.Combine(dir, "config.json"));
            foreach (var name in new[] { "\"gameSpeed\": 2", "\"moveSpeed\": 1.5", "\"damage\": 10", "\"taken\": 0", "\"medicine\": true", "\"fly\": true", "\"playerOps\"", "\"kind\": \"teleport\"", "\"x\": 1", "\"z\": 3" })
                StringAssert.Contains(text, name);
            var read = SuiteConfigStore.Read(dir);
            Assert.AreEqual(config.Cheats, read.Cheats);
            Assert.AreEqual(1, read.PlayerOps!.Count);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [TestMethod]
    public void Ops_FreshSequenceCapAndKinds()
    {
        var config = SuiteConfig.Default;
        for (var i = 0; i < 35; i++) config = PlayerEdits.Append(config, new PlayerOp(0, "nudge", Z: i));
        Assert.AreEqual(30, config.PlayerOps!.Count);
        Assert.IsTrue(config.PlayerOps.Zip(config.PlayerOps.Skip(1)).All(p => p.Second.Seq == p.First.Seq + 1));
        Assert.ThrowsException<ArgumentException>(() => PlayerEdits.Append(config, new PlayerOp(0, "explode")));
    }

    [TestMethod]
    public void Waypoints_RoundTrip()
    {
        var file = Path.Combine(Path.GetTempPath(), "pbz-wp-" + Guid.NewGuid().ToString("N"), "teleports.json");
        try
        {
            Assert.AreEqual(0, WaypointStore.Load(file).Count);
            WaypointStore.Save(file, [new Waypoint("密道口", "PBZ_Ximeng_WP", -211529, 98749, 5103)]);
            var back = WaypointStore.Load(file);
            Assert.AreEqual("密道口", back[0].Name);
            Assert.AreEqual(5103, back[0].Z);
            File.WriteAllText(file, "broken");
            Assert.AreEqual(0, WaypointStore.Load(file).Count);
        }
        finally { if (Directory.Exists(Path.GetDirectoryName(file))) Directory.Delete(Path.GetDirectoryName(file)!, true); }
    }

    [TestMethod]
    public void Telemetry_ParsesPlayer()
    {
        var line = """
            {"schemaVersion":1,"sessionId":"s","sequence":1,"monotonicMs":5,"lifecycle":"playing","suiteVersion":"1.0.3",
             "player":{"valid":true,"world":"PBZ_Ximeng_WP","position":{"x":-211529,"y":98749,"z":5103},"xp":1200,"level":7,
                       "gameSpeed":2,"fly":true,"results":[{"seq":9,"kind":"teleport","ok":true,"from":{"x":1,"y":2,"z":3}},
                                                          {"seq":10,"kind":"xp","ok":false,"error":"no attributes"}]}}
            """.Replace("\n", "").Replace("\r", "");
        var p = Snapshot.TryParse(line)!.Player!;
        Assert.IsTrue(p.Valid && p.Fly);
        Assert.AreEqual("PBZ_Ximeng_WP", p.World);
        Assert.AreEqual(5103, p.Z);
        Assert.AreEqual(7, p.Level);
        Assert.AreEqual(3, p.Results[0].FromZ);
        Assert.AreEqual("no attributes", p.Results[1].Error);
        Assert.IsNull(Snapshot.TryParse("""{"schemaVersion":1,"sessionId":"s","sequence":1,"monotonicMs":5}""")!.Player);
    }

    [TestMethod]
    public void Hotkeys_NewActionsHaveDistinctDefaults()
    {
        Assert.AreEqual(0, LockSettings.Default.Duplicates().Count);
        foreach (var a in new[] { LockActions.SavePos, LockActions.Teleport, LockActions.Fly, LockActions.FlyUp, LockActions.FlyDown })
            Assert.IsNotNull(Hotkey.Parse(LockSettings.Default.HotkeyText(a)), a);
    }
}
