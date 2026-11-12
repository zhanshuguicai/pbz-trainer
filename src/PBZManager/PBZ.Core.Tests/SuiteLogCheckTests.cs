using PBZ.Core.Suite;

namespace PBZ.Core.Tests;

[TestClass]
public sealed class SuiteLogCheckTests
{
    // Shape of the real 2026-11-07 failure (a constant removed by mistake stopped the rest of main.lua).
    private const string Failed = """
        [08:30:41] Mod 'PBZSuite' has enabled.txt, starting mod.
        [08:30:41] Error executing script: ...\PBZ\platform\steam\ue4ss\Mods\PBZSuite\Scripts\main.lua:796: 
        No overload found for function 'LoopInGameThreadWithDelay'.
        Overloads:
        """;

    [TestMethod]
    public void ReportsErrorSinceLastStart()
    {
        var err = SuiteLogCheck.LastStartError(Failed)!;
        StringAssert.Contains(err, "main.lua:796");
        StringAssert.Contains(err, "No overload found");
    }

    [TestMethod]
    public void FindsSuiteErrorAfterAnotherModsError()
    {
        var log = "[1] Mod 'PBZSuite' has enabled.txt, starting mod.\n[2] Error executing script: .../Mods/PBZProbe/Scripts/adhoc.lua:3:\nboom\n"
            + "[3] Error executing script: .../Mods/PBZSuite/Scripts/main.lua:796:\nNo overload found\n";
        StringAssert.Contains(SuiteLogCheck.LastStartError(log)!, "main.lua:796");
    }

    [TestMethod]
    public void IgnoresErrorsBeforeTheLastStartAndOtherMods()
    {
        Assert.IsNull(SuiteLogCheck.LastStartError(Failed + "\n[09:00:00] Mod 'PBZSuite' has enabled.txt, starting mod.\n[09:00:01] [Lua] [PBZSuite] v1.0.2 loaded\n"));
        Assert.IsNull(SuiteLogCheck.LastStartError("[1] Mod 'PBZSuite' has enabled.txt, starting mod.\n[2] Error executing script: .../Mods/OtherMod/main.lua:3:\nboom\n"));
        Assert.IsNull(SuiteLogCheck.LastStartError("no suite here"));
    }
}
