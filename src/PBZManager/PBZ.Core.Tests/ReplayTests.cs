using PBZ.Core.Events;
using PBZ.Core.Music;
using PBZ.Core.Telemetry;

namespace PBZ.Core.Tests;

// Replays the real 19-minute recording from T4a (evidence/t4a). Known facts about that run:
// one heavy fight dropped health from 1349 to 133 (crossing half at about t=467 s), flasks never reached 0,
// the player never died, and the pawn was re-created twice (generations 2 and 3) with 850/977.5 load-in values.
[TestClass]
public sealed class ReplayTests
{
    private static List<Snapshot> Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Data", "t4a-telemetry.jsonl");
        var lines = File.ReadAllLines(path);
        var list = lines.Select(Snapshot.TryParse).OfType<Snapshot>().ToList();
        Assert.AreEqual(lines.Length, list.Count, "every recorded line should parse");
        Assert.IsTrue(list.Count > 5000);
        return list;
    }

    private static List<EngineEvent> Replay(List<Snapshot> snapshots)
    {
        var engine = new EventEngine();
        var events = snapshots.SelectMany(engine.Process).ToList();
        foreach (var e in events) Console.WriteLine($"{e.MonotonicMs / 1000.0,8:F1}s #{e.BattleId,-3} {e.Kind,-14} {e.Reason}");
        return events;
    }

    // The heavy fight crosses half twice: at about 467 s, and again at about 535 s after healing back up.
    [TestMethod]
    public void T4a_HalfHealthCrossings_AllInTheHeavyFight()
    {
        var events = Replay(Load());
        var half = events.Where(e => e.Kind == EngineEventKind.HealthBelow).ToList();
        Assert.AreEqual(2, half.Count);
        Assert.IsTrue(half[0].MonotonicMs is >= 466_000 and <= 468_000, $"first at {half[0].MonotonicMs} ms");
        Assert.IsTrue(half[1].MonotonicMs is >= 534_000 and <= 537_000, $"second at {half[1].MonotonicMs} ms");
        Assert.AreEqual(half[0].BattleId, half[1].BattleId);
    }

    [TestMethod]
    public void T4a_NoFlasksEmptyAndNoDeath()
    {
        var events = Replay(Load());
        Assert.AreEqual(0, events.Count(EngineEventKind.FlasksEmpty));
        Assert.AreEqual(0, events.Count(EngineEventKind.Death));
    }

    [TestMethod]
    public void T4a_TwoRespawns()
    {
        Assert.AreEqual(2, Replay(Load()).Count(EngineEventKind.Respawn));
    }

    [TestMethod]
    public void T4a_BattlesArePairedAndPlayerChangesEndThem()
    {
        var events = Replay(Load());
        var starts = events.Count(EngineEventKind.BattleStarted);
        var ends = events.Count(EngineEventKind.BattleEnded);
        Assert.IsTrue(starts >= 5, $"only {starts} battles");
        Assert.IsTrue(ends == starts || ends == starts - 1, $"{starts} starts vs {ends} ends");
        // The heavy fight dips InCombat for a moment but must stay a single battle.
        var heavy = events.First(e => e.Kind == EngineEventKind.HealthBelow).BattleId;
        var heavyEnd = events.First(e => e.Kind == EngineEventKind.BattleEnded && e.BattleId == heavy);
        Assert.IsTrue(heavyEnd.MonotonicMs > 500_000, $"heavy fight ended early at {heavyEnd.MonotonicMs} ms");
    }

    [TestMethod]
    public void T4a_GenerationChangesNeverProduceHealthOrFlaskEvents()
    {
        var snapshots = Load();
        var engine = new EventEngine();
        for (var i = 0; i < snapshots.Count; i++)
        {
            var events = engine.Process(snapshots[i]);
            var newGeneration = i > 0 && snapshots[i].PlayerGeneration != snapshots[i - 1].PlayerGeneration;
            if (newGeneration)
                Assert.IsFalse(events.Any(e => e.Kind is EngineEventKind.HealthBelow or EngineEventKind.FlasksEmpty or EngineEventKind.Death));
        }
    }

    // The migrated default rules must behave like the T4d logic on the same recording: aizo once, then a fade.
    [TestMethod]
    public void T4a_DefaultRules_PlayAizoOnceAndFadeAtBattleEnd()
    {
        var log = new List<string>();
        var output = new RecordingOutput(log);
        var engine = new EventEngine();
        var player = new ConditionPlayer(output) { Enabled = true, Rules = MusicSettings.DefaultRules("aizo.mp3", "yuai.mp3") };
        engine.HealthThresholds = player.HealthThresholds;
        foreach (var s in Load()) player.Handle(engine.Process(s));
        CollectionAssert.AreEqual(new[] { "play half50", "fade" }, log);
    }

    private sealed class RecordingOutput(List<string> log) : IAudioOutput
    {
        public string? CurrentKey { get; private set; }
        public void Play(string key, PlayRequest r) { CurrentKey = key; log.Add("play " + key); }
        public void FadeOut() { CurrentKey = null; log.Add("fade"); }
    }
}
