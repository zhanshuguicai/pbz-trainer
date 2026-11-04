using PBZ.Core.Telemetry;

namespace PBZ.Core.Tests;

[TestClass]
public sealed class TelemetryTests
{
    private const string PlayingLine =
        """{"combat":{"dead":0,"inCombat":2,"valid":true},"configRevision":3,"errors":[],"flask":{"bottom":{"count":4,"id":"Kuye","valid":true},"top":{"count":0,"id":"None","valid":true},"total":4,"valid":true},"gameBuild":"26266538","health":{"current":620.5,"max":1241.425,"valid":true},"lifecycle":"playing","modules":{"items":false,"parry":false,"telemetry":true},"monotonicMs":41043,"playerGeneration":2,"schemaVersion":1,"sequence":5,"sessionId":"6aea1449e87e","suiteVersion":"0.1.0","wallTime":1793725528}""";

    private const string NoPlayerLine =
        """{"combat":{"dead":null,"inCombat":null,"valid":false},"configRevision":0,"errors":[],"flask":{"bottom":null,"top":null,"total":null,"valid":false},"gameBuild":"26266538","health":{"current":null,"max":null,"valid":false},"lifecycle":"noPlayer","modules":{"items":false,"parry":false,"telemetry":true},"monotonicMs":41249,"playerGeneration":0,"schemaVersion":1,"sequence":6,"sessionId":"6aea1449e87e","suiteVersion":"0.1.0","wallTime":1793725528}""";

    [TestMethod]
    public void Parse_PlayingLine()
    {
        var s = Snapshot.TryParse(PlayingLine)!;
        Assert.AreEqual("6aea1449e87e", s.SessionId);
        Assert.AreEqual(5, s.Sequence);
        Assert.IsTrue(s.IsPlaying);
        Assert.AreEqual(0.5, s.Health.Ratio!.Value, 1e-3);
        Assert.AreEqual(2, s.Combat.InCombat);
        Assert.AreEqual("None", s.Flask.Top!.Id);
        Assert.AreEqual(4, s.Flask.Total);
        Assert.AreEqual(3, s.ConfigRevision);
    }

    [TestMethod]
    public void Parse_NoPlayerLine_HasNullsAndInvalidGroups()
    {
        var s = Snapshot.TryParse(NoPlayerLine)!;
        Assert.IsFalse(s.IsPlaying);
        Assert.IsNull(s.Health.Ratio);
        Assert.IsFalse(s.Combat.Valid);
        Assert.IsNull(s.Flask.Top);
        Assert.IsNull(s.Flask.Total);
    }

    [TestMethod]
    public void Parse_RejectsGarbageAndOtherSchemas()
    {
        Assert.IsNull(Snapshot.TryParse("{\"sequence\":"));
        Assert.IsNull(Snapshot.TryParse("[]"));
        Assert.IsNull(Snapshot.TryParse(PlayingLine.Replace("\"schemaVersion\":1", "\"schemaVersion\":2")));
    }

    [TestMethod]
    public void Tailer_SkipsExistingHistory_ThenReadsAppendedLinesAndKeepsPartialLine()
    {
        var dir = Directory.CreateTempSubdirectory("pbz-tailer-").FullName;
        try
        {
            var file = Path.Combine(dir, "telemetry-6aea1449e87e-0.jsonl");
            File.WriteAllText(file, PlayingLine + "\n");
            var tailer = new TelemetryTailer(dir);
            Assert.AreEqual(0, tailer.ReadNew().Count, "history before attaching is skipped");

            using (var w = new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
            using (var sw = new StreamWriter(w))
            {
                sw.Write(NoPlayerLine + "\n" + PlayingLine.Substring(0, 40));
            }
            var got = tailer.ReadNew();
            Assert.AreEqual(1, got.Count);
            Assert.AreEqual(6, got[0].Sequence);

            File.AppendAllText(file, PlayingLine.Substring(40) + "\n");
            got = tailer.ReadNew();
            Assert.AreEqual(1, got.Count, "the partial line is completed on the next read");
            Assert.AreEqual(5, got[0].Sequence);
        }
        finally { Directory.Delete(dir, true); }
    }

    [TestMethod]
    public void Tailer_SwitchesToNewerSessionByName()
    {
        var dir = Directory.CreateTempSubdirectory("pbz-tailer-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "telemetry-6aea1449e87e-0.jsonl"), "");
            var tailer = new TelemetryTailer(dir);
            tailer.ReadNew();
            var newer = Path.Combine(dir, "telemetry-6aeb0000aaaa-0.jsonl");
            File.WriteAllText(newer, PlayingLine.Replace("6aea1449e87e", "6aeb0000aaaa") + "\n");
            var got = tailer.ReadNew();
            Assert.AreEqual(newer, tailer.CurrentFile);
            Assert.AreEqual(1, got.Count, "a new file is read from its start");
        }
        finally { Directory.Delete(dir, true); }
    }

    [TestMethod]
    public void SortKey_ParsesSessionAndPart()
    {
        Assert.AreEqual(("6aea1449e87e", 3), TelemetryTailer.SortKey("telemetry-6aea1449e87e-3"));
        Assert.IsNull(TelemetryTailer.SortKey("config"));
    }
}
