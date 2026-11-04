using System.Text.Json;
using PBZ.Core.Suite;

namespace PBZ.Core.Items;

public sealed record InventoryItem(string Id, string? Name, int? Amount, int? HasMax, int? Type, int? Detail, int? Material, bool Editable)
{
    // Category names from the game's enums (EItemMaterialType, EItemDetailType, EItemTypes; survey 2026-11-04).
    public string Category => Material switch
    {
        1 => "武器强化材料",
        2 => "杀器强化材料",
        3 => "武器摄魂材料",
        4 => "杀器摄魂材料",
        5 => "饰品强化材料",
        6 => "药强化材料",
        7 => "核心养成材料",
        _ => Type switch
        {
            1 => "书",
            2 => "书页",
            3 => "信件",
            4 => "记忆之花",
            5 => "花瓣",
            _ => Detail switch
            {
                1 => "其他材料",
                2 => "剧情物品",
                3 => "烹饪",
                4 => "珍宝",
                5 => "属性道具",
                6 => "饰品碎片",
                _ => "其他",
            },
        },
    };

    // Ordering for the list: editable materials first, by category, then id.
    public int SortKey => Material is >= 1 and <= 7 ? Material.Value : 100 + (Type ?? 0) * 10 + (Detail ?? 0);
}

public sealed record ItemOpResult(long Seq, string? Kind, string? Id, long? Target, bool Ok, long? Before, long? After, string? Error);

// runtime/inventory.json written by PBZSuite 0.3.0+ when asked (InventoryRequest) and after edits.
public sealed record InventorySnapshot(long Seq, string SessionId, string Reason, bool Valid, long? Money,
    IReadOnlyList<InventoryItem> Items, IReadOnlyList<ItemOpResult> Results)
{
    public const long MoneyMax = 99_999_999;
    public const int ItemMax = 9999;

    // Null if the file is missing, half written or not this format; callers just try again later.
    public static InventorySnapshot? TryRead(string runtimeDir)
    {
        var path = Path.Combine(runtimeDir, "inventory.json");
        try
        {
            if (!File.Exists(path)) return null;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var doc = JsonDocument.Parse(stream);
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object || Long(r, "seq") is not { } seq || Str(r, "sessionId") is not { } session) return null;
            var items = new List<InventoryItem>();
            if (r.TryGetProperty("items", out var arr) && arr.ValueKind == JsonValueKind.Array)
                foreach (var i in arr.EnumerateArray())
                    if (Str(i, "id") is { } id)
                        items.Add(new InventoryItem(id, Str(i, "name"), Int(i, "amount"), Int(i, "hasMax"), Int(i, "type"), Int(i, "detail"),
                            Int(i, "material"), i.TryGetProperty("editable", out var ed) && ed.ValueKind == JsonValueKind.True));
            var results = new List<ItemOpResult>();
            if (r.TryGetProperty("results", out var res) && res.ValueKind == JsonValueKind.Array)
                foreach (var x in res.EnumerateArray())
                    if (Long(x, "seq") is { } s)
                        results.Add(new ItemOpResult(s, Str(x, "kind"), Str(x, "id"), Long(x, "target"),
                            x.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True, Long(x, "before"), Long(x, "after"), Str(x, "error")));
            return new InventorySnapshot(seq, session, Str(r, "reason") ?? "", r.TryGetProperty("valid", out var v) && v.ValueKind == JsonValueKind.True,
                Long(r, "money"), items, results);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static long? Long(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? (long)v.GetDouble() : null;

    private static int? Int(JsonElement e, string name) => Long(e, name) is { } l ? (int)l : null;
}

public static class ItemEdits
{
    // Appends edits to the config's op list with fresh sequence numbers; keeps the last 50 so the file stays small.
    // The game applies each sequence number once and ignores ops that were already in the file when it started.
    public static SuiteConfig Append(SuiteConfig config, IEnumerable<(string Kind, string? Id, long Target)> edits)
    {
        var ops = new List<ItemOp>(config.ItemOps ?? []);
        var next = ops.Count == 0 ? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() : ops.Max(o => o.Seq) + 1;
        foreach (var (kind, id, target) in edits)
        {
            var max = kind == "money" ? InventorySnapshot.MoneyMax : InventorySnapshot.ItemMax;
            ops.Add(new ItemOp(next++, kind, id, Math.Clamp(target, 0, max)));
        }
        if (ops.Count > 50) ops = ops.Skip(ops.Count - 50).ToList();
        return config with { ItemOps = ops };
    }
}
