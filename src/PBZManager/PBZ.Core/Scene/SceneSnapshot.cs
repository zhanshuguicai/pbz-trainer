using System.Text.Json;
using PBZ.Core.Suite;

namespace PBZ.Core.Scene;

// One nearby mechanism listed by PBZSuite 2.0.0 (runtime/scene.json). Path is the object path the game finds it by.
public sealed record SceneActor(string Path, string Class, int Distance, string? Desc, string? Flow, bool Hidden, bool Collision, bool Saved)
{
    public SceneKnown.Entry? Known => SceneKnown.Find(this);
    public string DisplayName => Known?.Name ?? SceneKnown.Guess(Class);
    public string ShortName => Path.Contains('.') ? Path[(Path.LastIndexOf('.') + 1)..] : Path;
}

public sealed record SceneOpResult(long Seq, string? Kind, string? Path, bool Ok, bool? Hidden, bool? Collision, bool? Dissolved, string? Error);

public sealed record SceneSnapshot(long Seq, string SessionId, string Reason, bool Valid, string? World,
    IReadOnlyList<SceneActor> Actors, IReadOnlyList<SceneOpResult> Results)
{
    // Null if the file is missing, half written or not this format; callers just try again later.
    public static SceneSnapshot? TryRead(string runtimeDir)
    {
        var path = System.IO.Path.Combine(runtimeDir, "scene.json");
        try
        {
            if (!File.Exists(path)) return null;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var doc = JsonDocument.Parse(stream);
            return Parse(doc.RootElement);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static SceneSnapshot? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return Parse(doc.RootElement);
        }
        catch (JsonException) { return null; }
    }

    private static SceneSnapshot? Parse(JsonElement r)
    {
        if (r.ValueKind != JsonValueKind.Object || Long(r, "seq") is not { } seq || Str(r, "sessionId") is not { } session) return null;
        var actors = new List<SceneActor>();
        if (r.TryGetProperty("actors", out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var a in arr.EnumerateArray())
                if (Str(a, "path") is { } p && Str(a, "class") is { } cls)
                    actors.Add(new SceneActor(p, cls, (int)(Long(a, "distance") ?? 0), Str(a, "desc"), Str(a, "flow"),
                        Bool(a, "hidden") == true, Bool(a, "collision") != false, Bool(a, "saved") == true));
        var results = new List<SceneOpResult>();
        if (r.TryGetProperty("results", out var res) && res.ValueKind == JsonValueKind.Array)
            foreach (var x in res.EnumerateArray())
                if (Long(x, "seq") is { } s)
                    results.Add(new SceneOpResult(s, Str(x, "kind"), Str(x, "path"), Bool(x, "ok") == true, Bool(x, "hidden"),
                        Bool(x, "collision"), Bool(x, "dissolved"), Str(x, "error")));
        return new SceneSnapshot(seq, session, Str(r, "reason") ?? "", Bool(r, "valid") == true, Str(r, "world"), actors, results);
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static long? Long(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? (long)v.GetDouble() : null;

    private static bool? Bool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) ? v.ValueKind switch { JsonValueKind.True => true, JsonValueKind.False => false, _ => null } : null;
}

// Mechanisms we have looked at in the game, with a readable name and what to do about them.
public static class SceneKnown
{
    public sealed record Entry(string Name, string Hint, string Action);

    private static readonly (string Class, string? Flow, Entry Entry)[] Table =
    [
        // 2026-11-11, 戏梦山庄: the painting over the 冷图密道 secret passage; its dissolve can only be triggered once.
        ("BP_Guahua_Huanshu_C", null, new("幻术挂画（挡住密道）", "画后是密道。点“打开 / 溶解”让画消失并可穿过。", "dissolve")),
    ];

    public static Entry? Find(SceneActor actor) =>
        Table.Where(t => t.Class == actor.Class && (t.Flow is null || t.Flow == actor.Flow)).Select(t => t.Entry).FirstOrDefault();

    // A rough Chinese name from the blueprint class name, for everything not in the table.
    public static string Guess(string cls) => cls switch
    {
        _ when cls.Contains("Guahua") => "挂画",
        _ when cls.Contains("PingZhang") => "幻术屏障",
        _ when cls.Contains("Door") => "门",
        _ when cls.Contains("Chest") => "宝箱",
        _ when cls.Contains("Switch") => "机关",
        _ when cls.Contains("Wall") => "墙",
        _ when cls.Contains("Block") => "阻挡物",
        _ when cls.Contains("Interact") => "可交互物",
        _ => "场景物件",
    };
}

// One scene repair for the game: Kind "dissolve", "remove" or "restore" on the actor at Path; each Seq is applied once.
public sealed record SceneOp(long Seq, string Kind, string Path);

public static class SceneEdits
{
    public static readonly IReadOnlySet<string> Kinds = new HashSet<string> { "dissolve", "remove", "restore" };

    // Appends ops with fresh sequence numbers and keeps the last 30, as ItemEdits does.
    public static SuiteConfig Append(SuiteConfig config, IEnumerable<(string Kind, string Path)> edits)
    {
        var ops = new List<SceneOp>(config.SceneOps ?? []);
        var next = ops.Count == 0 ? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() : ops.Max(o => o.Seq) + 1;
        foreach (var (kind, path) in edits)
        {
            if (!Kinds.Contains(kind)) throw new ArgumentException("unknown scene op " + kind);
            ops.Add(new SceneOp(next++, kind, path));
        }
        if (ops.Count > 30) ops = ops.Skip(ops.Count - 30).ToList();
        return config with { SceneOps = ops };
    }
}
