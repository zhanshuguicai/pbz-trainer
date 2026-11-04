using PBZ.Core.Events;
using PBZ.Core.Music;
using PBZ.Core.Telemetry;

namespace PBZ.Core.Tests;

// New triggers (2026-11-04): engine edges, rule matching, fade triggers, extra conditions and settings migration.
[TestClass]
public sealed class TriggerTests
{
    private sealed class FakeOutput : IAudioOutput
    {
        public List<string> Log { get; } = [];
        public string? CurrentKey { get; private set; }
        public void Play(string key, PlayRequest r) { CurrentKey = key; Log.Add("play " + key); Loops.Add(r.Loop); Requests.Add(r); }
        public List<PlayRequest> Requests { get; } = [];
        public List<bool> Loops { get; } = [];
        public void FadeOut() { CurrentKey = null; Log.Add("fade"); }
    }

    private static Dictionary<EngineEventKind, IReadOnlyCollection<double>> W(params (EngineEventKind k, double v)[] items) =>
        items.GroupBy(i => i.k).ToDictionary(g => g.Key, g => (IReadOnlyCollection<double>)g.Select(i => i.v).ToList());

    private static List<EngineEvent> Start(EventEngine engine, Feed feed)
    {
        var all = feed.Run(engine, 2); // first sample of a generation is skipped
        feed.Combat = 1;
        all.AddRange(feed.Run(engine, 2));
        return all;
    }

    [TestMethod]
    public void Engine_HealthAboveAndBigHit()
    {
        var engine = new EventEngine { Watch = W((EngineEventKind.HealthAbove, 0.8), (EngineEventKind.BigHit, 0.25)) };
        var feed = new Feed();
        Start(engine, feed);
        feed.Hp = 700; // lost 30 % at once
        var hit = feed.Run(engine, 1);
        Assert.AreEqual(1, hit.Count(EngineEventKind.BigHit));
        feed.Hp = 600; // only 10 %
        Assert.AreEqual(0, feed.Run(engine, 1).Count(EngineEventKind.BigHit));
        feed.Hp = 850;
        var up = feed.Run(engine, 2);
        Assert.AreEqual(1, up.Count(EngineEventKind.HealthAbove));
    }

    [TestMethod]
    public void Engine_FlaskUsedRefilledAndEmpty()
    {
        var engine = new EventEngine();
        var feed = new Feed { Top = 1, Bottom = 0 };
        feed.Run(engine, 2);
        feed.Top = 0;
        var e = feed.Run(engine, 1);
        Assert.AreEqual(1, e.Count(EngineEventKind.FlaskUsed));
        Assert.AreEqual(1, e.Count(EngineEventKind.FlasksEmpty));
        feed.Top = 3;
        Assert.AreEqual(1, feed.Run(engine, 1).Count(EngineEventKind.FlasksRefilled));
        feed.FlaskValid = false; // unknown never reports
        Assert.AreEqual(0, feed.Run(engine, 2).Count);
    }

    [TestMethod]
    public void Engine_EnemiesAtLeastAndBattleLong()
    {
        var engine = new EventEngine { Watch = W((EngineEventKind.EnemiesAtLeast, 3), (EngineEventKind.BattleLong, 2)) };
        var feed = new Feed();
        Start(engine, feed);
        feed.Combat = 3;
        var e = feed.Run(engine, 1);
        Assert.AreEqual(1, e.Count(EngineEventKind.EnemiesAtLeast));
        Assert.AreEqual(0, feed.Run(engine, 1).Count(EngineEventKind.EnemiesAtLeast)); // stays at 3: no repeat
        var later = feed.Run(engine, 10); // 2 s more
        Assert.AreEqual(1, later.Count(EngineEventKind.BattleLong));
    }

    [TestMethod]
    public void Engine_ValueEdges()
    {
        var engine = new EventEngine();
        var feed = new Feed();
        ValuesSample V(double shachi, double ult, double spirit) =>
            new(new(shachi, 480, true), new(ult, 100, true), new(spirit, 5, true));
        feed.Values = V(300, 90, 2);
        feed.Run(engine, 2);
        feed.Values = V(480, 100, 5);
        var full = feed.Run(engine, 1);
        Assert.AreEqual(1, full.Count(EngineEventKind.ShachiFull));
        Assert.AreEqual(1, full.Count(EngineEventKind.UltimateReady));
        Assert.AreEqual(1, full.Count(EngineEventKind.SpiritFull));
        feed.Values = V(480, 3, 0);
        var spent = feed.Run(engine, 1);
        Assert.AreEqual(1, spent.Count(EngineEventKind.UltimateUsed));
        Assert.AreEqual(1, spent.Count(EngineEventKind.SpiritEmpty));
        feed.Values = null; // older suite: nothing
        Assert.AreEqual(0, feed.Run(engine, 2).Count);
    }

    [TestMethod]
    public void Player_FadeTriggerAndParameters()
    {
        var output = new FakeOutput();
        var rule = new PlayRule("crowd", "围攻", true, RuleTrigger.EnemiesAtLeast, 3, "a.mp3", 1, RuleScope.EveryTime,
            RuleTrigger.HealthAbove, 80);
        var player = new ConditionPlayer(output) { Enabled = true, Rules = [rule] };
        Assert.AreEqual(3, player.Watch[EngineEventKind.EnemiesAtLeast].Single());
        Assert.AreEqual(0.8, player.Watch[EngineEventKind.HealthAbove].Single(), 1e-9);

        player.Handle([new EngineEvent(EngineEventKind.EnemiesAtLeast, 1, 0, 0, "", 2)]); // other threshold
        Assert.AreEqual(0, output.Log.Count);
        player.Handle([new EngineEvent(EngineEventKind.EnemiesAtLeast, 1, 0, 0, "", 3)]);
        player.Handle([new EngineEvent(EngineEventKind.BattleEnded, 1, 0, 0, "left combat")]); // not this rule's fade
        player.Handle([new EngineEvent(EngineEventKind.HealthAbove, 1, 0, 0, "", 0.8)]);
        CollectionAssert.AreEqual(new[] { "play crowd", "fade" }, output.Log);
        CollectionAssert.AreEqual(new[] { true }, output.Loops); // has a fade trigger: loops until it fades
    }

    [TestMethod]
    public void Player_NoFadeTriggerPlaysToTheEnd()
    {
        var output = new FakeOutput();
        var rule = new PlayRule("x", "x", true, RuleTrigger.UltimateUsed, 0, "x.mp3", 1, RuleScope.EveryTime, null);
        var player = new ConditionPlayer(output) { Enabled = true, Rules = [rule] };
        player.Handle([new EngineEvent(EngineEventKind.UltimateUsed, 1, 0, 0, "")]);
        player.Handle([new EngineEvent(EngineEventKind.BattleEnded, 1, 0, 0, "death")]);
        CollectionAssert.AreEqual(new[] { "play x" }, output.Log);
        CollectionAssert.AreEqual(new[] { false }, output.Loops); // "play to the end": once
    }

    [TestMethod]
    public void Player_ConditionsFilterStartOnly()
    {
        var output = new FakeOutput();
        var player = new ConditionPlayer(output) { Enabled = true, Rules = MusicSettings.DefaultRules("aizo.mp3", "yuai.mp3") };
        var empty = new EngineEvent(EngineEventKind.FlasksEmpty, 0, 0, 0, "");
        player.Handle([empty], new RuleState(false, 0.9, 0, 0)); // out of battle: default rule now needs a battle
        Assert.AreEqual(0, output.Log.Count);
        player.Handle([empty with { BattleId = 2 }], new RuleState(true, 0.9, 2, 0));
        CollectionAssert.AreEqual(new[] { "play flasks0" }, output.Log);

        var c = new RuleConditions(BattleFilter.Any, HealthBelowPercent: 30, EnemiesAtLeast: 2);
        Assert.IsTrue(c.Holds(new RuleState(true, 0.2, 2, 5)));
        Assert.IsFalse(c.Holds(new RuleState(true, 0.4, 2, 5)));
        Assert.IsFalse(c.Holds(new RuleState(true, 0.2, null, 5))); // unknown fails
        Assert.AreEqual("血量<30%，敌人≥2", c.Summary());
    }

    [TestMethod]
    public void Settings_MigratesT7Rules()
    {
        var dir = Directory.CreateTempSubdirectory("pbz-music-").FullName;
        var file = Path.Combine(dir, "settings.json");
        File.WriteAllText(file, """
            {"enabled":true,"volume":0.39,"fadeOutMs":2000,"rules":[
              {"id":"half50","name":"半血","enabled":true,"trigger":"HealthBelow","thresholdPercent":50,"trackPath":"a.mp3","priority":1,"scope":"OncePerBattle","fadeOnBattleEnd":true},
              {"id":"flasks0","name":"药喝空","enabled":true,"trigger":"FlasksEmpty","thresholdPercent":0,"trackPath":"b.mp3","priority":2,"scope":"OncePerBattle","fadeOnBattleEnd":true},
              {"id":"x1","name":"阵亡","enabled":true,"trigger":"Death","thresholdPercent":0,"trackPath":"c.mp3","priority":3,"scope":"EveryTime","fadeOnBattleEnd":false}]}
            """);
        var s = MusicSettings.Load(file);
        var rules = s.Rules!;
        Assert.AreEqual(RuleTrigger.BattleOver, rules[0].StopTrigger);
        Assert.IsTrue(rules[0].When.IsEmpty);
        Assert.AreEqual(BattleFilter.InBattle, rules[1].When.Battle);
        Assert.IsNull(rules[2].StopTrigger);
        Assert.AreEqual(0.39, s.Volume);

        s.Save(file);
        var text = File.ReadAllText(file);
        Assert.IsFalse(text.Contains("fadeOnBattleEnd"));
        Assert.IsFalse(text.Contains("\"when\""));
        var again = MusicSettings.Load(file).Rules!;
        Assert.AreEqual(BattleFilter.InBattle, again[1].When.Battle);
        Assert.IsNull(again[2].StopTrigger);
        Directory.Delete(dir, true);
    }
}
