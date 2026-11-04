using System.Text.Json;
using System.Text.Json.Serialization;

namespace PBZ.Core.Suite;

// runtime/config.json, read by PBZSuite once a second. Lua applies a file only when `revision` changes.
public sealed record SuiteModules(bool Parry, bool Items);

// Value lock switches (T8). The game applies an item only when Enabled, the item and Compatible are all true.
// *Value is the number to hold; FullValue (-1) means "refill to the current max". The game caps values at the max.
public sealed record SuiteLock(bool Enabled, bool Health, bool Shachi, bool Ultimate, bool Spirit,
    double HealthValue = SuiteLock.FullValue, double ShachiValue = SuiteLock.FullValue,
    double UltimateValue = SuiteLock.FullValue, double SpiritValue = SuiteLock.FullValue)
{
    public const double FullValue = -1;
    public static SuiteLock Off => new(false, false, false, false, false);
    [JsonIgnore] public bool AnyActive => Enabled && (Health || Shachi || Ultimate || Spirit);
}

// Auto deflect / dodge options (T5b). The master switch is SuiteModules.Parry. LeadMs: press this long before the hit.
public sealed record SuiteParry(bool Deflect = true, bool Dodge = true, int LeadMs = 120)
{
    public const int MaxLeadMs = 300;
}

// One item edit for the game (T5a). Kind "item" (Id = inventory id) or "money"; the game applies each Seq once.
public sealed record ItemOp(long Seq, string Kind, string? Id, long Target);

public sealed record SuiteConfig(int Revision, bool Compatible, SuiteModules Modules, string ParryHotkey, SuiteLock? Lock = null,
    int InventoryRequest = 0, List<ItemOp>? ItemOps = null, SuiteParry? Parry = null)
{
    public static SuiteConfig Default => new(0, false, new SuiteModules(false, false), "F7", SuiteLock.Off);
}

public static class SuiteConfigStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static SuiteConfig Read(string runtimeDir)
    {
        var path = Path.Combine(runtimeDir, "config.json");
        try
        {
            var read = File.Exists(path) ? JsonSerializer.Deserialize<SuiteConfig>(File.ReadAllText(path), Json) : null;
            if (read is null) return SuiteConfig.Default;
            // Older files (hand-written during T4a) lack newer fields; fill them with defaults.
            return read with
            {
                Modules = read.Modules ?? SuiteConfig.Default.Modules,
                ParryHotkey = string.IsNullOrWhiteSpace(read.ParryHotkey) ? SuiteConfig.Default.ParryHotkey : read.ParryHotkey,
                Lock = read.Lock ?? SuiteLock.Off,
                Parry = read.Parry ?? new SuiteParry(),
            };
        }
        catch (JsonException)
        {
            return SuiteConfig.Default;
        }
    }

    // Writes a new revision through a temp file and an atomic replace, so the game never reads half a file.
    public static SuiteConfig Write(string runtimeDir, SuiteConfig config)
    {
        Directory.CreateDirectory(runtimeDir);
        var path = Path.Combine(runtimeDir, "config.json");
        var next = config with { Revision = Math.Max(config.Revision, Read(runtimeDir).Revision) + 1 };
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(next, Json));
        if (File.Exists(path)) File.Replace(tmp, path, null);
        else File.Move(tmp, path);
        return next;
    }
}
