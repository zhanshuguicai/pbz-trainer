using PBZ.Core.Scene;
using PBZ.Core.Suite;

namespace PBZ.Core.Tests;

[TestClass]
public sealed class SceneTests
{
    // Shaped like what PBZSuite 2.0.0 writes; the painting is the real one from 戏梦山庄 (probe 2026-11-11).
    private const string Json = """
        {"seq":3,"sessionId":"abc","reason":"edit","valid":true,"world":"PBZ_Ximeng_WP","actors":[
          {"path":"/Game/Maps/B08_09_12_13/PBZ_Ximeng_WP.PBZ_Ximeng_WP:PersistentLevel.BP_SlenderDoor_C_UAID_1","class":"BP_SlenderDoor_C","distance":107,"desc":null,"flow":null,"hidden":false,"collision":true,"saved":false},
          {"path":"/Game/Maps/B08_09_12_13/PBZ_Ximeng_WP.PBZ_Ximeng_WP:PersistentLevel.BP_Guahua_Huanshu_C_UAID_2","class":"BP_Guahua_Huanshu_C","distance":173,"desc":"TestDescd","flow":"BPF_LengTuMiDao","hidden":false,"collision":true,"saved":true}],
         "results":[{"seq":10,"kind":"dissolve","path":"x","ok":true,"hidden":false,"collision":true,"dissolved":true},
                    {"seq":11,"kind":"remove","path":"y","ok":false,"error":"not found"}]}
        """;

    [TestMethod]
    public void ParsesActorsAndResults()
    {
        var s = SceneSnapshot.Parse(Json)!;
        Assert.AreEqual(3, s.Seq);
        Assert.IsTrue(s.Valid);
        Assert.AreEqual(2, s.Actors.Count);
        var painting = s.Actors[1];
        Assert.AreEqual("BPF_LengTuMiDao", painting.Flow);
        Assert.AreEqual("BP_Guahua_Huanshu_C_UAID_2", painting.ShortName);
        Assert.AreEqual("dissolve", painting.Known!.Action);
        Assert.AreEqual("门", s.Actors[0].DisplayName);
        Assert.IsNull(s.Actors[0].Known);
        Assert.IsTrue(s.Results[0].Dissolved == true && s.Results[0].Ok);
        Assert.AreEqual("not found", s.Results[1].Error);
    }

    [TestMethod]
    public void RejectsOtherFiles()
    {
        Assert.IsNull(SceneSnapshot.Parse("{\"items\":[]}"));
        Assert.IsNull(SceneSnapshot.Parse("not json"));
    }

    [TestMethod]
    public void AppendsOpsWithFreshSequenceAndCap()
    {
        var config = SuiteConfig.Default;
        for (var i = 0; i < 35; i++) config = SceneEdits.Append(config, [("remove", "p" + i)]);
        Assert.AreEqual(30, config.SceneOps!.Count);
        Assert.AreEqual("p34", config.SceneOps[^1].Path);
        Assert.IsTrue(config.SceneOps.Zip(config.SceneOps.Skip(1)).All(p => p.Second.Seq == p.First.Seq + 1));
        Assert.ThrowsException<ArgumentException>(() => SceneEdits.Append(config, [("explode", "p")]));
    }

    [TestMethod]
    public void ConfigRoundTripsSceneFields()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pbz-scene-" + Guid.NewGuid().ToString("N"));
        try
        {
            var written = SuiteConfigStore.Write(dir, SceneEdits.Append(SuiteConfig.Default with { SceneRequest = 4 }, [("dissolve", "/Game/x")]));
            var text = File.ReadAllText(Path.Combine(dir, "config.json"));
            StringAssert.Contains(text, "\"sceneRequest\": 4");
            StringAssert.Contains(text, "\"kind\": \"dissolve\"");
            var read = SuiteConfigStore.Read(dir);
            Assert.AreEqual(4, read.SceneRequest);
            Assert.AreEqual(written.SceneOps![0].Seq, read.SceneOps![0].Seq);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
