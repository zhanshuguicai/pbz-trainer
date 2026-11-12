using PBZ.Core.Events;
using PBZ.Core.Music;

namespace PBZ.Core.Tests;

[TestClass]
public sealed class ConditionPlayerTests
{
    private sealed class FakeOutput : IAudioOutput
    {
        public List<string> Log { get; } = [];
        public string? CurrentKey { get; private set; }
        public void Play(string key, PlayRequest r) { CurrentKey = key; Log.Add("play " + key); Loops.Add(r.Loop); Requests.Add(r); }
        public List<PlayRequest> Requests { get; } = [];
        public List<bool> Loops { get; } = [];
        public void FadeOut() { CurrentKey = null; Log.Add("fade"); }
        public void Finish() => CurrentKey = null;
    }

    private static EngineEvent E(EngineEventKind kind, int battle = 1, string reason = "", double? threshold = null) =>
        new(kind, battle, 0, 0, reason, threshold);

    private static EngineEvent Half(int battle = 1) => E(EngineEventKind.HealthBelow, battle, "", 0.5);

    private static (ConditionPlayer player, FakeOutput output) Defaults()
    {
        var output = new FakeOutput();
        var player = new ConditionPlayer(output) { Enabled = true, Rules = MusicSettings.DefaultRules("aizo.mp3", "yuai.mp3") };
        return (player, output);
    }

    [TestMethod]
    public void Disabled_DoesNothing()
    {
        var (player, output) = Defaults();
        player.Enabled = false;
        player.Handle([Half()]);
        Assert.AreEqual(0, output.Log.Count);
    }

    [TestMethod]
    public void DefaultRules_HalfOncePerBattle()
    {
        var (player, output) = Defaults();
        player.Handle([Half()]);
        output.Finish();
        player.Handle([Half()]);
        player.Handle([Half(battle: 2)]);
        CollectionAssert.AreEqual(new[] { "play half50", "play half50" }, output.Log);
    }

    [TestMethod]
    public void DefaultRules_FlasksInterruptHalf_AndHalfDoesNotInterruptFlasks()
    {
        var (player, output) = Defaults();
        player.Handle([Half()]);
        player.Handle([E(EngineEventKind.FlasksEmpty)]);
        player.Handle([Half(battle: 2)]);
        CollectionAssert.AreEqual(new[] { "play half50", "play flasks0" }, output.Log);
    }

    [TestMethod]
    public void DefaultRules_SameSample_FlasksWin_AndHalfIsUsedUp()
    {
        var (player, output) = Defaults();
        player.Handle([E(EngineEventKind.FlasksEmpty), Half()]);
        output.Finish();
        player.Handle([Half()]);
        CollectionAssert.AreEqual(new[] { "play flasks0" }, output.Log);
    }

    [TestMethod]
    public void BattleEnd_FadesRulesThatAskForIt()
    {
        var (player, output) = Defaults();
        player.Handle([E(EngineEventKind.BattleEnded, reason: "left combat")]);
        player.Handle([Half()]);
        player.Handle([E(EngineEventKind.BattleEnded, reason: "death")]);
        CollectionAssert.AreEqual(new[] { "play half50", "fade" }, output.Log);
    }

    [TestMethod]
    public void DeathRule_PlaysAfterTheBattleMusicFades()
    {
        var (player, output) = Defaults();
        player.Rules = [.. player.Rules, new PlayRule("dead", "阵亡", true, RuleTrigger.Death, 0, "dead.mp3", 3, RuleScope.EveryTime, null)];
        player.Handle([Half()]);
        player.Handle([E(EngineEventKind.Death), E(EngineEventKind.BattleEnded, reason: "death")]);
        CollectionAssert.AreEqual(new[] { "play half50", "fade", "play dead" }, output.Log);
    }

    [TestMethod]
    public void LeavingCombatRule_OnlyOnRealLeave()
    {
        var (player, output) = Defaults();
        player.Rules = [new PlayRule("leave", "脱战", true, RuleTrigger.BattleEnded, 0, "leave.mp3", 1, RuleScope.EveryTime, null)];
        player.Handle([E(EngineEventKind.BattleEnded, reason: "player changed")]);
        player.Handle([E(EngineEventKind.BattleEnded, reason: "left combat")]);
        CollectionAssert.AreEqual(new[] { "play leave" }, output.Log);
    }

    [TestMethod]
    public void HealthRules_MatchTheirOwnThreshold()
    {
        var (player, output) = Defaults();
        player.Rules = [.. player.Rules, new PlayRule("low20", "残血", true, RuleTrigger.HealthBelow, 20, "low.mp3", 5, RuleScope.OncePerBattle)];
        CollectionAssert.AreEquivalent(new[] { 0.5, 0.2 }, player.HealthThresholds.ToArray());
        player.Handle([Half()]);
        player.Handle([E(EngineEventKind.HealthBelow, 1, "", 0.2)]);
        CollectionAssert.AreEqual(new[] { "play half50", "play low20" }, output.Log);
    }

    [TestMethod]
    public void EveryTimeScope_RepeatsWithinABattle()
    {
        var (player, output) = Defaults();
        player.Rules = [new PlayRule("start", "开战", true, RuleTrigger.FlasksEmpty, 0, "x.mp3", 1, RuleScope.EveryTime)];
        player.Handle([E(EngineEventKind.FlasksEmpty)]);
        output.Finish(); // the first song is over
        player.Handle([E(EngineEventKind.FlasksEmpty)]);
        Assert.AreEqual(2, output.Log.Count);
    }

    [TestMethod]
    public void PlayingSong_OnlyInterruptedByHigherPriority()
    {
        var (player, output) = Defaults();
        player.Rules =
        [
            new PlayRule("a", "A", true, RuleTrigger.FlaskUsed, 0, "a.mp3", 2, RuleScope.EveryTime),
            new PlayRule("same", "同级", true, RuleTrigger.UltimateUsed, 0, "b.mp3", 2, RuleScope.EveryTime),
            new PlayRule("low", "更低", true, RuleTrigger.SpiritEmpty, 0, "c.mp3", 1, RuleScope.EveryTime),
            new PlayRule("high", "更高", true, RuleTrigger.ShachiFull, 0, "d.mp3", 3, RuleScope.EveryTime),
        ];
        player.Handle([E(EngineEventKind.FlaskUsed)]);
        player.Handle([E(EngineEventKind.FlaskUsed)]);    // same rule again: keeps playing
        player.Handle([E(EngineEventKind.UltimateUsed)]); // same priority: does not replace
        player.Handle([E(EngineEventKind.SpiritEmpty)]);  // lower: does not replace
        CollectionAssert.AreEqual(new[] { "play a" }, output.Log);
        player.Handle([E(EngineEventKind.ShachiFull)]);   // higher: interrupts
        CollectionAssert.AreEqual(new[] { "play a", "play high" }, output.Log);
    }

    [TestMethod]
    public void RuleWithoutTrack_ReportsProblem()
    {
        var (player, output) = Defaults();
        player.Rules = [PlayRule.New(RuleTrigger.BattleStarted)];
        string? problem = null;
        player.Problem += (_, msg) => problem = msg;
        player.Handle([E(EngineEventKind.BattleStarted)]);
        Assert.AreEqual(0, output.Log.Count);
        Assert.IsNotNull(problem);
    }

    [TestMethod]
    public void Settings_MigrateT4dFormat()
    {
        var dir = Directory.CreateTempSubdirectory("pbz-settings-").FullName;
        try
        {
            var file = Path.Combine(dir, "settings.json");
            File.WriteAllText(file, """{"enabled":true,"halfHealthPath":"E:\\a.mp3","flasksEmptyPath":"E:\\b.mp3","volume":0.6,"fadeOutMs":2000}""");
            var s = MusicSettings.Load(file);
            Assert.IsTrue(s.Enabled);
            Assert.AreEqual(2, s.Rules!.Count);
            Assert.AreEqual(@"E:\a.mp3", s.Rules[0].TrackPath);
            Assert.AreEqual(@"E:\b.mp3", s.Rules[1].TrackPath);
            s.Save(file);
            var again = MusicSettings.Load(file);
            Assert.AreEqual(RuleTrigger.FlasksEmpty, again.Rules![1].Trigger);
            StringAssert.Contains(File.ReadAllText(file), "\"FlasksEmpty\"");
            Assert.IsFalse(File.ReadAllText(file).Contains("halfHealthPath"));
        }
        finally { Directory.Delete(dir, true); }
    }
}
