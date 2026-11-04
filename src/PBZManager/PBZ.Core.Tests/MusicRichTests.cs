using PBZ.Core.Events;
using PBZ.Core.Music;
using PBZ.Core.Telemetry;

namespace PBZ.Core.Tests;

// Boss triggers and per-rule playback options (2026-11-04, before 1.0).
[TestClass]
public sealed class MusicRichTests
{
    private sealed class Out : IAudioOutput
    {
        public List<PlayRequest> Played { get; } = [];
        public List<string> Log { get; } = [];
        public string? CurrentKey { get; private set; }
        public void Play(string key, PlayRequest r) { CurrentKey = key; Played.Add(r); Log.Add("play " + key); }
        public void FadeOut() { CurrentKey = null; Log.Add("fade"); }
        public void Finish() => CurrentKey = null;
    }

    private static BossSample Boss(string id, double hp, double max = 1000, string name = "魔天") => new(true, 1, id, name, hp, max, true);
    private static readonly BossSample NoBoss = new(false, 0, null, null, null, null, true);

    [TestMethod]
    public void Engine_BossStartBelowDefeatedEnd()
    {
        var engine = new EventEngine { Watch = new Dictionary<EngineEventKind, IReadOnlyCollection<double>> { [EngineEventKind.BossHealthBelow] = [0.5] } };
        var feed = new Feed();
        feed.Run(engine, 2);
        feed.Boss = Boss("a", 1000);
        var start = feed.Run(engine, 1);
        Assert.AreEqual(1, start.Count(EngineEventKind.BossFightStarted));
        Assert.AreEqual("魔天", start.Single(e => e.Kind == EngineEventKind.BossFightStarted).Reason);
        Assert.IsTrue(engine.BossActive);
        feed.Boss = Boss("a", 400);
        Assert.AreEqual(1, feed.Run(engine, 1).Count(EngineEventKind.BossHealthBelow));
        feed.Boss = Boss("a", 0);
        Assert.AreEqual(1, feed.Run(engine, 1).Count(EngineEventKind.BossDefeated));
        feed.Boss = NoBoss;
        var end = feed.Run(engine, 1);
        Assert.AreEqual(0, end.Count(EngineEventKind.BossDefeated), "defeat reported once");
        Assert.AreEqual("defeated", end.Single(e => e.Kind == EngineEventKind.BossFightEnded).Reason);
        Assert.IsFalse(engine.BossActive);
    }

    [TestMethod]
    public void Engine_BossGoneOrUnreadable()
    {
        var engine = new EventEngine();
        var feed = new Feed();
        feed.Run(engine, 2);
        feed.Boss = Boss("a", 800);
        feed.Run(engine, 1);
        feed.Boss = new BossSample(false, 0, null, null, null, null, false); // HUD unreadable: keep the fight
        Assert.AreEqual(0, feed.Run(engine, 3).Count);
        feed.Boss = NoBoss; // player died / walked away
        var end = feed.Run(engine, 1);
        Assert.AreEqual("gone", end.Single(e => e.Kind == EngineEventKind.BossFightEnded).Reason);
        Assert.AreEqual(0, end.Count(EngineEventKind.BossDefeated));
        feed.Boss = Boss("b", 900, name: "第二个"); // a new fight
        Assert.AreEqual(1, feed.Run(engine, 1).Count(EngineEventKind.BossFightStarted));
    }

    [TestMethod]
    public void Player_BossHalfRuleAndBossCondition()
    {
        var output = new Out();
        var half = new PlayRule("bh", "Boss 半血", true, RuleTrigger.BossHealthBelow, 50, "b.mp3", 3, RuleScope.EveryTime, RuleTrigger.BossFightEnded);
        var onlyBoss = new PlayRule("hb", "Boss 战里残血", true, RuleTrigger.HealthBelow, 30, "h.mp3", 1, RuleScope.EveryTime,
            Conditions: new RuleConditions(Boss: BossFilter.InBossFight));
        var player = new ConditionPlayer(output) { Enabled = true, Rules = [half, onlyBoss] };
        Assert.AreEqual(0.5, player.Watch[EngineEventKind.BossHealthBelow].Single(), 1e-9);

        player.Handle([new EngineEvent(EngineEventKind.HealthBelow, 1, 0, 0, "", 0.3)], new RuleState(true, 0.2, 1, 3, BossActive: false));
        Assert.AreEqual(0, output.Played.Count, "no boss fight: condition fails");
        player.Handle([new EngineEvent(EngineEventKind.BossHealthBelow, 1, 0, 0, "", 0.5)], new RuleState(true, 0.9, 1, 3, true, 0.5));
        player.Handle([new EngineEvent(EngineEventKind.BossFightEnded, 1, 0, 0, "defeated")], new RuleState(true, 0.9, 1, 3));
        CollectionAssert.AreEqual(new[] { "play bh", "fade" }, output.Log);
        Assert.IsTrue(output.Played[0].Loop, "auto loop: has a fade trigger");

        var c = new RuleConditions(BossHealthBelowPercent: 30);
        Assert.IsTrue(c.Holds(new RuleState(true, 1, 1, 1, true, 0.2)));
        Assert.IsFalse(c.Holds(new RuleState(true, 1, 1, 1, false, 0.2)));
        Assert.AreEqual("Boss 血量<30%", c.Summary());
    }

    [TestMethod]
    public void Player_CooldownUsesGameTime()
    {
        var output = new Out();
        var rule = new PlayRule("u", "绝技", true, RuleTrigger.UltimateUsed, 0, "u.mp3", 1, RuleScope.EveryTime, null,
            Playback: new RulePlayback(CooldownSeconds: 10));
        var player = new ConditionPlayer(output) { Enabled = true, Rules = [rule] };
        EngineEvent At(long ms) => new(EngineEventKind.UltimateUsed, 1, 0, ms, "");
        player.Handle([At(1000)]);
        output.Finish();
        player.Handle([At(5000)]);   // 4 s later: cooling down
        player.Handle([At(11_500)]); // 10.5 s later: plays again
        Assert.AreEqual(2, output.Played.Count);
    }

    [TestMethod]
    public void Player_TracksTakeTurnsOrShuffleWithoutRepeats()
    {
        var output = new Out();
        string[] songs = ["a.mp3", "b.mp3", "c.mp3"];
        var turns = new PlayRule("t", "t", true, RuleTrigger.FlaskUsed, 0, songs[0], 1, RuleScope.EveryTime, null,
            Tracks: [.. songs], Playback: new RulePlayback(Shuffle: false));
        var player = new ConditionPlayer(output) { Enabled = true, Rules = [turns] };
        for (var i = 0; i < 4; i++) { player.Handle([new EngineEvent(EngineEventKind.FlaskUsed, 0, 0, i, "")]); output.Finish(); }
        CollectionAssert.AreEqual(new[] { "a.mp3", "b.mp3", "c.mp3", "a.mp3" }, output.Played.Select(p => p.Path).ToArray());

        var shuffled = new Out();
        var p2 = new ConditionPlayer(shuffled, new Random(7)) { Enabled = true, Rules = [turns with { Playback = new RulePlayback() }] };
        for (var i = 0; i < 30; i++) { p2.Handle([new EngineEvent(EngineEventKind.FlaskUsed, 0, 0, i, "")]); shuffled.Finish(); }
        var paths = shuffled.Played.Select(p => p.Path).ToList();
        for (var i = 1; i < paths.Count; i++) Assert.AreNotEqual(paths[i - 1], paths[i], "no song twice in a row");
        Assert.AreEqual(3, paths.Distinct().Count());
    }

    [TestMethod]
    public void Request_AppliesPlaybackOptions()
    {
        var rule = new PlayRule("r", "r", true, RuleTrigger.BattleStarted, 0, "x.mp3", 1, RuleScope.EveryTime, null,
            Playback: new RulePlayback(VolumePercent: 150, StartSeconds: 12.5, Loop: LoopMode.Loop, FadeInSeconds: 2, FadeOutSeconds: 4));
        var r = ConditionPlayer.Request(rule, "x.mp3");
        Assert.IsTrue(r.Loop, "explicit loop even without a fade trigger");
        Assert.AreEqual(1.5, r.VolumeFactor, 1e-9);
        Assert.AreEqual(12.5, r.StartSeconds);
        Assert.AreEqual(2, r.FadeInSeconds);
        Assert.AreEqual(4.0, r.FadeOutSeconds);
        Assert.IsFalse(ConditionPlayer.Request(rule with { StopTrigger = RuleTrigger.BattleOver, Playback = new RulePlayback(Loop: LoopMode.Once) }, "x").Loop);
        Assert.AreEqual(200, new RulePlayback(VolumePercent: 999).Clamped().VolumePercent);
    }

    [TestMethod]
    public void Settings_SaveTracksPlaybackAndBossConditions()
    {
        var dir = Directory.CreateTempSubdirectory("pbz-rich-").FullName;
        var file = Path.Combine(dir, "settings.json");
        var rule = new PlayRule("x", "x", true, RuleTrigger.BossFightStarted, 0, "a.mp3", 2, RuleScope.OncePerBattle, RuleTrigger.BossFightEnded,
            Conditions: new RuleConditions(Boss: BossFilter.InBossFight, BossHealthBelowPercent: 80),
            Tracks: ["a.mp3", "b.mp3"], Playback: new RulePlayback(VolumePercent: 80, Shuffle: false));
        new MusicSettings(true, 0.5, 2000, [rule]).Save(file);
        var text = File.ReadAllText(file);
        Assert.IsFalse(text.Contains("\"allTracks\"") || text.Contains("\"how\""), "computed properties are not saved");
        var back = MusicSettings.Load(file).Rules!.Single();
        CollectionAssert.AreEqual(new[] { "a.mp3", "b.mp3" }, back.AllTracks.ToArray());
        Assert.AreEqual(80, back.How.VolumePercent);
        Assert.IsFalse(back.How.Shuffle);
        Assert.AreEqual(BossFilter.InBossFight, back.When.Boss);
        Assert.AreEqual(80.0, back.When.BossHealthBelowPercent);
        Assert.AreEqual(RuleTrigger.BossFightEnded, back.StopTrigger);
        Directory.Delete(dir, true);
    }

    [TestMethod]
    public void Snapshot_ReadsBoss()
    {
        const string line = """{"schemaVersion":1,"sessionId":"x","sequence":1,"monotonicMs":5,"lifecycle":"playing","boss":{"active":true,"count":2,"id":"123","name":"魔天","current":250,"max":1000,"valid":true}}""";
        var b = Snapshot.TryParse(line)!.Boss!;
        Assert.IsTrue(b.Active);
        Assert.AreEqual(2, b.Count);
        Assert.AreEqual(0.25, b.Ratio!.Value, 1e-9);
        Assert.AreEqual("魔天", b.Name);
    }
}
