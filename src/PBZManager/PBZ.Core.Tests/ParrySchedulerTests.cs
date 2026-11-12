using PBZ.Core.Combat;
using PBZ.Core.Suite;

namespace PBZ.Core.Tests;

// 1.0.2 auto deflect: commands carry their own timing; the scheduler turns them into key down / up events.
[TestClass]
public sealed class ParrySchedulerTests
{
    private static ParryCommand Cmd(long seq, string kind, int pressIn, int hitIn) =>
        ParryCommand.TryParse($"PBZPARRY s {seq} {kind} {pressIn} {hitIn} end")!;

    // Runs the scheduler millisecond by millisecond and records (time, event).
    private static List<(long T, KeyEvent E)> Run(ParryScheduler s, long from, long to, bool foreground = true)
    {
        var all = new List<(long, KeyEvent)>();
        for (var t = from; t <= to; t++) foreach (var e in s.Tick(t, foreground)) all.Add((t, e));
        return all;
    }

    [TestMethod]
    public void Parse_NewFormatHoldAndMultiLine()
    {
        var c = Cmd(7, "deflect", 160, 280);
        Assert.AreEqual(160, c.PressInMs);
        Assert.AreEqual(280, c.HitInMs);
        Assert.IsFalse(c.Dodge || c.Hold);
        Assert.IsTrue(Cmd(8, "hold", 0, 100).Hold);
        Assert.IsTrue(Cmd(9, "dodge", 0, 100).Dodge);
        var old = ParryCommand.TryParse("PBZPARRY s 3 deflect end")!; // 1.0.1 line: press now
        Assert.AreEqual(0, old.PressInMs);
        var all = ParryCommand.ParseAll("PBZPARRY s 1 deflect 10 100 end\nPBZPARRY s 2 dodge 5 50 end\nPBZPARRY s 3 defl");
        CollectionAssert.AreEqual(new long[] { 1, 2 }, all.Select(x => x.Seq).ToArray(), "half-written last line is skipped");
    }

    [TestMethod]
    public void Tap_PressesAtScheduledTimeForTapMs()
    {
        var s = new ParryScheduler();
        s.Add(Cmd(1, "deflect", 160, 280), 1000);
        var ev = Run(s, 1000, 1300);
        CollectionAssert.AreEqual(new[] { (1160L, new KeyEvent(false, true)), (1160L + ParryScheduler.TapMs, new KeyEvent(false, false)) }, ev);
        Assert.AreEqual(1, s.Presses);
    }

    [TestMethod]
    public void Combo_TwoHitsCloseTogetherBothPressedOnTime()
    {
        var s = new ParryScheduler();
        s.Add(Cmd(1, "deflect", 100, 220), 0);
        s.Add(Cmd(2, "deflect", 120, 240), 0); // 20 ms later: the first tap is still down
        s.Add(Cmd(3, "deflect", 200, 320), 0); // key already up again: pressed right on time
        var ev = Run(s, 0, 400);
        var downs = ev.Where(x => x.E.Down).Select(x => x.T).ToList();
        CollectionAssert.AreEqual(new long[] { 100, 120 + ParryScheduler.RepressGapMs, 200 }, downs, "a short release then a new press, not 80 ms late");
        Assert.AreEqual(3, s.Presses);
        Assert.IsFalse(ev[^1].E.Down, "ends released");
    }

    [TestMethod]
    public void Hold_KeepsKeyDownUntilAfterTheLastHit()
    {
        var s = new ParryScheduler();
        s.Add(Cmd(1, "hold", 100, 220), 0);
        var ev = Run(s, 0, 500);
        Assert.AreEqual((100L, new KeyEvent(false, true)), ev[0]);
        Assert.AreEqual((220L + ParryScheduler.HoldTailMs, new KeyEvent(false, false)), ev[1]);

        var combo = new ParryScheduler { ComboHold = true };
        combo.Add(Cmd(1, "deflect", 100, 220), 0);
        combo.Add(Cmd(2, "deflect", 200, 320), 0);
        var cev = Run(combo, 0, 600);
        Assert.AreEqual(2, cev.Count, "one down, one up for the whole combo");
        Assert.AreEqual(320L + ParryScheduler.HoldTailMs, cev[1].T);
    }

    [TestMethod]
    public void TapDuringHold_RepressesButKeepsHolding()
    {
        var s = new ParryScheduler();
        s.Add(Cmd(1, "hold", 0, 300), 0);      // held until 300 + tail
        s.Add(Cmd(2, "deflect", 200, 250), 0);  // a normal hit before the held one lands
        var ev = Run(s, 0, 600);
        var last = ev.Last(x => !x.E.Down);
        Assert.AreEqual(300L + ParryScheduler.HoldTailMs, last.T, "the hold still covers its own hit");
        Assert.AreEqual(2, ev.Count(x => x.E.Down), "re-pressed once for the tap");
    }

    [TestMethod]
    public void Dodge_AlwaysTapsEvenInComboHold()
    {
        var s = new ParryScheduler { ComboHold = true };
        s.Add(Cmd(1, "dodge", 50, 400), 0);
        var ev = Run(s, 0, 200);
        CollectionAssert.AreEqual(new[] { (50L, new KeyEvent(true, true)), (50L + ParryScheduler.TapMs, new KeyEvent(true, false)) }, ev);
    }

    [TestMethod]
    public void NotForeground_SkipsAndNeverLeavesAKeyDown()
    {
        var s = new ParryScheduler();
        s.Add(Cmd(1, "hold", 10, 300), 0);
        var ev = Run(s, 0, 50);
        Assert.IsTrue(ev.Single().E.Down);
        var after = Run(s, 51, 52, foreground: false);
        Assert.IsFalse(after.Single().E.Down, "released when the game loses focus");
        s.Add(Cmd(2, "deflect", 0, 100), 60);
        Assert.AreEqual(0, Run(s, 60, 70, foreground: false).Count);
        Assert.AreEqual(1, s.SkippedNotForeground);
    }

    [TestMethod]
    public void SuiteParry_ComparesActionContents()
    {
        var a = new SuiteParry(Actions: new() { ["4"] = "dodge" });
        var b = new SuiteParry(Actions: new() { ["4"] = "dodge" });
        Assert.IsTrue(a.SameAs(b));
        Assert.IsFalse(a.SameAs(b with { Actions = new() { ["4"] = "deflect" } }));
        Assert.IsTrue(new SuiteParry().SameAs(new SuiteParry(Actions: new())));
        Assert.AreEqual(6, AttackTypes.All.Count);
    }
}
