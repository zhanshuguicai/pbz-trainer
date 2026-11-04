using PBZ.Core.Events;

namespace PBZ.Core.Tests;

// Edges the engine must report (or must not). "Once per battle" and priorities are tested in ConditionPlayerTests.
[TestClass]
public sealed class EventEngineRuleTests
{
    private static (EventEngine engine, Feed feed, List<EngineEvent> events) InBattle()
    {
        var engine = new EventEngine();
        var feed = new Feed { Combat = 1 };
        var events = feed.Run(engine, 3);
        Assert.AreEqual(1, events.Count(EngineEventKind.BattleStarted));
        return (engine, feed, events);
    }

    [TestMethod]
    public void HealthBelow_FiresOnDownwardCrossingOnly()
    {
        var (engine, feed, events) = InBattle();
        feed.Hp = 600; events.AddRange(feed.Run(engine, 2));
        feed.Hp = 500; events.AddRange(feed.Run(engine, 1));
        feed.Hp = 300; events.AddRange(feed.Run(engine, 2));
        Assert.AreEqual(1, events.Count(EngineEventKind.HealthBelow));
    }

    [TestMethod]
    public void HealthBelow_ReportsEveryCrossing_EvenInTheSameBattle()
    {
        var (engine, feed, events) = InBattle();
        feed.Hp = 400; events.AddRange(feed.Run(engine, 2));
        feed.Hp = 1000; events.AddRange(feed.Run(engine, 2));
        feed.Hp = 400; events.AddRange(feed.Run(engine, 2));
        Assert.AreEqual(2, events.Count(EngineEventKind.HealthBelow));
    }

    [TestMethod]
    public void HealthBelow_EachConfiguredThreshold()
    {
        var (engine, feed, events) = InBattle();
        engine.HealthThresholds = [0.5, 0.2];
        feed.Hp = 100; events.AddRange(feed.Run(engine, 1)); // one big hit crosses both
        var crossed = events.Where(e => e.Kind == EngineEventKind.HealthBelow).Select(e => e.Threshold).ToList();
        CollectionAssert.AreEqual(new double?[] { 0.5, 0.2 }, crossed);
    }

    [TestMethod]
    public void HealthBelow_NewBattle_FiresAgain()
    {
        var (engine, feed, events) = InBattle();
        feed.Hp = 400; events.AddRange(feed.Run(engine, 2));
        feed.Combat = 0; feed.Hp = 1000; events.AddRange(feed.Run(engine, 30)); // 6 s at 0 ends the battle
        Assert.AreEqual(1, events.Count(EngineEventKind.BattleEnded));
        feed.Combat = 2; events.AddRange(feed.Run(engine, 3));
        feed.Hp = 400; events.AddRange(feed.Run(engine, 2));
        Assert.AreEqual(2, events.Count(EngineEventKind.BattleStarted));
        Assert.AreEqual(2, events.Count(EngineEventKind.HealthBelow));
    }

    [TestMethod]
    public void HealthBelow_EnteringBattleAlreadyBelow_DoesNotFire()
    {
        var engine = new EventEngine();
        var feed = new Feed { Hp = 400 };
        var events = feed.Run(engine, 3);
        feed.Combat = 1; events.AddRange(feed.Run(engine, 5));
        feed.Hp = 300; events.AddRange(feed.Run(engine, 3));
        Assert.AreEqual(0, events.Count(EngineEventKind.HealthBelow));
    }

    [TestMethod]
    public void HealthBelow_EnteringBelow_ThenHealAndDrop_Fires()
    {
        var engine = new EventEngine();
        var feed = new Feed { Hp = 400, Combat = 1 };
        var events = feed.Run(engine, 4);
        feed.Hp = 800; events.AddRange(feed.Run(engine, 2));
        feed.Hp = 450; events.AddRange(feed.Run(engine, 2));
        Assert.AreEqual(1, events.Count(EngineEventKind.HealthBelow));
    }

    [TestMethod]
    public void HealthBelow_OutOfCombat_DoesNotFire()
    {
        var engine = new EventEngine();
        var feed = new Feed();
        var events = feed.Run(engine, 3);
        feed.Hp = 300; events.AddRange(feed.Run(engine, 3));
        Assert.AreEqual(0, events.Count(EngineEventKind.HealthBelow));
    }

    [TestMethod]
    public void HealthBelow_ExactlyAtThreshold_Counts()
    {
        var (engine, feed, events) = InBattle();
        feed.Hp = 500; events.AddRange(feed.Run(engine, 1));
        Assert.AreEqual(1, events.Count(EngineEventKind.HealthBelow));
    }

    [TestMethod]
    public void Flasks_PositiveToZero_Fires()
    {
        var (engine, feed, events) = InBattle();
        feed.Top = 0; feed.Bottom = 1; events.AddRange(feed.Run(engine, 2));
        feed.Bottom = 0; events.AddRange(feed.Run(engine, 5));
        Assert.AreEqual(1, events.Count(EngineEventKind.FlasksEmpty));
    }

    [TestMethod]
    public void Flasks_OnlyOneSlotEmpty_DoesNotFire()
    {
        var (engine, feed, events) = InBattle();
        feed.Top = 0; events.AddRange(feed.Run(engine, 3));
        Assert.AreEqual(0, events.Count(EngineEventKind.FlasksEmpty));
    }

    [TestMethod]
    public void Flasks_StartingAtZero_DoesNotFire()
    {
        var engine = new EventEngine();
        var feed = new Feed { Top = 0, Bottom = 0 };
        var events = feed.Run(engine, 3);
        feed.Combat = 1; events.AddRange(feed.Run(engine, 5));
        Assert.AreEqual(0, events.Count(EngineEventKind.FlasksEmpty));
    }

    [TestMethod]
    public void Flasks_UnknownBetweenPositiveAndZero_DoesNotFire()
    {
        var (engine, feed, events) = InBattle();
        feed.FlaskValid = false; events.AddRange(feed.Run(engine, 2));
        feed.FlaskValid = true; feed.Top = 0; feed.Bottom = 0; events.AddRange(feed.Run(engine, 3));
        Assert.AreEqual(0, events.Count(EngineEventKind.FlasksEmpty));
    }

    [TestMethod]
    public void Flasks_OutOfCombat_FiresOnEachEmptying()
    {
        var engine = new EventEngine();
        var feed = new Feed { Top = 0, Bottom = 1 };
        var events = feed.Run(engine, 2);
        feed.Bottom = 0; events.AddRange(feed.Run(engine, 2));
        feed.Bottom = 4; events.AddRange(feed.Run(engine, 2)); // bell refill
        feed.Bottom = 0; events.AddRange(feed.Run(engine, 2));
        var empties = events.Where(e => e.Kind == EngineEventKind.FlasksEmpty).ToList();
        Assert.AreEqual(2, empties.Count);
        Assert.IsTrue(empties.All(e => e.BattleId == 0));
    }

    [TestMethod]
    public void CombatDipShorterThanConfirm_KeepsOneBattle()
    {
        var (engine, feed, events) = InBattle();
        feed.Combat = 0; events.AddRange(feed.Run(engine, 23)); // 4.6 s, like the T4a dip at t=365.6 s
        feed.Combat = 1; events.AddRange(feed.Run(engine, 3));
        feed.Hp = 400; events.AddRange(feed.Run(engine, 1));
        Assert.AreEqual(1, events.Count(EngineEventKind.BattleStarted));
        Assert.AreEqual(0, events.Count(EngineEventKind.BattleEnded));
        Assert.AreEqual(1, events.Count(EngineEventKind.HealthBelow));
    }

    [TestMethod]
    public void LoadingGap_DoesNotConfirmLeavingCombat()
    {
        var (engine, feed, events) = InBattle();
        feed.Combat = 0;
        events.AddRange(feed.Run(engine, 1));
        events.AddRange(feed.Run(engine, 1, stepMs: 10_000)); // 10 s with no samples
        events.AddRange(feed.Run(engine, 2));
        Assert.AreEqual(0, events.Count(EngineEventKind.BattleEnded));
        events.AddRange(feed.Run(engine, 26));
        Assert.AreEqual(1, events.Count(EngineEventKind.BattleEnded));
    }

    [TestMethod]
    public void SingleCombatSample_DoesNotStartBattle()
    {
        var engine = new EventEngine();
        var feed = new Feed();
        var events = feed.Run(engine, 2);
        feed.Combat = 1; events.AddRange(feed.Run(engine, 1));
        feed.Combat = 0; events.AddRange(feed.Run(engine, 2));
        Assert.AreEqual(0, events.Count(EngineEventKind.BattleStarted));
    }

    [TestMethod]
    public void UnknownCombat_NeverEndsBattle()
    {
        var (engine, feed, events) = InBattle();
        feed.CombatValid = false; events.AddRange(feed.Run(engine, 50));
        Assert.AreEqual(0, events.Count(EngineEventKind.BattleEnded));
    }

    [TestMethod]
    public void NoPlayer_NeverEndsBattleByItself()
    {
        var (engine, feed, events) = InBattle();
        feed.Lifecycle = "noPlayer"; events.AddRange(feed.Run(engine, 50));
        Assert.AreEqual(0, events.Count(EngineEventKind.BattleEnded));
    }

    [TestMethod]
    public void NewGeneration_EndsBattle_ReportsRespawn_AndIgnoresItsFirstSample()
    {
        var (engine, feed, events) = InBattle();
        Assert.AreEqual(0, events.Count(EngineEventKind.Respawn), "the first pawn seen is not a respawn");
        feed.Generation = 2; feed.Hp = 850; feed.Max = 977.5; events.AddRange(feed.Run(engine, 1));
        Assert.AreEqual(1, events.Count(EngineEventKind.BattleEnded));
        Assert.AreEqual(1, events.Count(EngineEventKind.Respawn));
        feed.Max = 1000; feed.Hp = 400; events.AddRange(feed.Run(engine, 4));
        Assert.AreEqual(0, events.Count(EngineEventKind.HealthBelow), "load-in values must not create a crossing");
    }

    [TestMethod]
    public void DeathTag_ReportsDeathOnce_ThenEndsBattle()
    {
        var (engine, feed, events) = InBattle();
        feed.Dead = 1; events.AddRange(feed.Run(engine, 5));
        var kinds = events.Skip(1).Select(e => e.Kind).ToList();
        CollectionAssert.AreEqual(new[] { EngineEventKind.Death, EngineEventKind.BattleEnded }, kinds);
        Assert.AreEqual("death", events.Last().Reason);
    }

    [TestMethod]
    public void HealthZero_ReportsDeath_WhenTagLags()
    {
        var (engine, feed, events) = InBattle();
        feed.Hp = 0; events.AddRange(feed.Run(engine, 3));
        Assert.AreEqual(1, events.Count(EngineEventKind.Death));
    }

    [TestMethod]
    public void Death_RearmsAfterRevive()
    {
        var (engine, feed, events) = InBattle();
        feed.Dead = 1; feed.Hp = 0; events.AddRange(feed.Run(engine, 2));
        feed.Dead = 0; feed.Hp = 1000; feed.Combat = 0; events.AddRange(feed.Run(engine, 2));
        feed.Hp = 0; events.AddRange(feed.Run(engine, 2));
        Assert.AreEqual(2, events.Count(EngineEventKind.Death));
    }

    [TestMethod]
    public void DuplicateSequence_IsIgnored()
    {
        var engine = new EventEngine();
        var feed = new Feed { Combat = 1 };
        var first = feed.Next();
        engine.Process(first);
        var events = engine.Process(first);
        events = [.. events, .. engine.Process(first)];
        Assert.AreEqual(0, events.Count, "repeating one sample must not count as consecutive combat samples");
    }

    [TestMethod]
    public void NewSession_ResetsBaseline()
    {
        var (engine, feed, events) = InBattle();
        feed.Session = "00000002bbbb"; feed.Hp = 400; events.AddRange(feed.Run(engine, 1));
        Assert.AreEqual(1, events.Count(EngineEventKind.BattleEnded));
        Assert.AreEqual(0, events.Count(EngineEventKind.Respawn), "a new game session is not a respawn");
        events.AddRange(feed.Run(engine, 4));
        Assert.AreEqual(0, events.Count(EngineEventKind.HealthBelow));
    }
}
