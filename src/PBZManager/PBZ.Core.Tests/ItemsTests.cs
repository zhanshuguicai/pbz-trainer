using PBZ.Core.Items;
using PBZ.Core.Suite;

namespace PBZ.Core.Tests;

[TestClass]
public sealed class ItemsTests
{
    [TestMethod]
    public void Inventory_ParsesGameFileAndCategories()
    {
        var dir = Directory.CreateTempSubdirectory("pbz-inv-").FullName;
        File.WriteAllText(Path.Combine(dir, "inventory.json"), """
            {"items":[{"amount":43,"detail":1,"editable":true,"hasMax":117,"id":"Weapon_Material_01","material":1,"name":"玄铁","type":0},
                      {"amount":1,"detail":2,"editable":false,"hasMax":1,"id":"I_01003","material":0,"name":null,"type":0},
                      {"amount":1,"detail":0,"editable":false,"hasMax":1,"id":"Page_XC_01","material":0,"type":2}],
             "money":15969,"reason":"edit","results":[{"after":99,"before":43,"id":"Weapon_Material_01","kind":"item","ok":true,"seq":7,"target":99}],
             "seq":3,"sessionId":"abc","valid":true,"wallTime":1}
            """);
        var s = InventorySnapshot.TryRead(dir)!;
        Assert.AreEqual(15969, s.Money);
        Assert.AreEqual(3, s.Items.Count);
        Assert.AreEqual("武器强化材料", s.Items[0].Category);
        Assert.IsTrue(s.Items[0].Editable);
        Assert.AreEqual("剧情物品", s.Items[1].Category);
        Assert.IsNull(s.Items[1].Name);
        Assert.AreEqual("书页", s.Items[2].Category);
        Assert.IsTrue(s.Results.Single().Ok);
        Assert.AreEqual(99, s.Results[0].After);

        File.WriteAllText(Path.Combine(dir, "inventory.json"), "{\"seq\":"); // half written
        Assert.IsNull(InventorySnapshot.TryRead(dir));
        Directory.Delete(dir, true);
    }

    [TestMethod]
    public void Edits_GetIncreasingSeqAndClamp()
    {
        var c = ItemEdits.Append(SuiteConfig.Default, [("item", "A", 50_000), ("money", null, -5)]);
        var ops = c.ItemOps!;
        Assert.AreEqual(2, ops.Count);
        Assert.AreEqual(ops[0].Seq + 1, ops[1].Seq);
        Assert.AreEqual(InventorySnapshot.ItemMax, ops[0].Target);
        Assert.AreEqual(0, ops[1].Target);
        var more = ItemEdits.Append(c, Enumerable.Range(0, 60).Select(i => ("item", (string?)$"X{i}", (long)i)));
        Assert.AreEqual(50, more.ItemOps!.Count);
        Assert.AreEqual(ops[1].Seq + 60, more.ItemOps!.Last().Seq);
    }

    [TestMethod]
    public void Config_ItemOpsRoundTripAndKeepLock()
    {
        var dir = Directory.CreateTempSubdirectory("pbz-cfgi-").FullName;
        var lockOn = new SuiteLock(true, true, true, true, true);
        SuiteConfigStore.Write(dir, SuiteConfig.Default with { Lock = lockOn });
        var withOps = ItemEdits.Append(SuiteConfigStore.Read(dir), [("item", "Weapon_Material_01", 99)]) with { InventoryRequest = 4 };
        SuiteConfigStore.Write(dir, withOps);
        var read = SuiteConfigStore.Read(dir);
        Assert.AreEqual(lockOn, read.Lock);
        Assert.AreEqual(4, read.InventoryRequest);
        Assert.AreEqual("Weapon_Material_01", read.ItemOps!.Single().Id);
        StringAssert.Contains(File.ReadAllText(Path.Combine(dir, "config.json")), "\"itemOps\"");
        Directory.Delete(dir, true);
    }
}
