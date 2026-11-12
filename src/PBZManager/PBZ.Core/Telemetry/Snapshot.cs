using System.Text.Json;

namespace PBZ.Core.Telemetry;

// One telemetry line written by PBZSuite (schema v1, see docs/adr/0001-architecture.md).
// Every field the game could not read is null and its group carries Valid = false.

public sealed record HealthSample(double? Current, double? Max, bool Valid)
{
    public double? Ratio => Valid && Current is { } c && Max is { } m && m > 0 ? c / m : null;
}

// Shachi / UltimateCharge / SpiritShadow with their max (suite 0.2.0+; absent in older lines, then all invalid).
public sealed record ValuesSample(HealthSample Shachi, HealthSample Ultimate, HealthSample Spirit)
{
    public static ValuesSample Unknown => new(new(null, null, false), new(null, null, false), new(null, null, false));
}

// The first boss whose health bar the HUD shows (suite 1.0.0+). Valid = health readable; Active = a bar is shown.
public sealed record BossSample(bool Active, int Count, string? Id, string? Name, double? Current, double? Max, bool Valid)
{
    public double? Ratio => Valid && Current is { } c && Max is { } m && m > 0 ? Math.Max(0, c / m) : null;
}

// Auto deflect counters since the game started (suite 0.4.0+; zero for older lines).
public sealed record ParrySample(int Deflects, int Dodges, int HitsTaken, int Clean);

public sealed record CombatSample(int? InCombat, int? Dead, bool Valid);

public sealed record FlaskSlot(string? Id, int? Count, bool Valid);

public sealed record FlaskSample(FlaskSlot? Top, FlaskSlot? Bottom, int? Total, bool Valid);

// Player tools (suite 1.0.3+): where the character is, XP / level, the game speed in effect, and one-shot op results.
public sealed record PlayerOpResult(long Seq, string? Kind, bool Ok, string? Error, double? FromX, double? FromY, double? FromZ,
    double? Before, double? After);

public sealed record PlayerSample(bool Valid, string? World, double? X, double? Y, double? Z, double? Xp, double? Level,
    double? GameSpeed, bool Fly, IReadOnlyList<PlayerOpResult> Results);

public sealed record ModulesState(bool Telemetry, bool Parry, bool Items, bool Lock = false);

public sealed record Snapshot(
    int SchemaVersion,
    string SessionId,
    long Sequence,
    long MonotonicMs,
    long? WallTime,
    string Lifecycle,
    int PlayerGeneration,
    HealthSample Health,
    CombatSample Combat,
    FlaskSample Flask,
    int ConfigRevision,
    ModulesState Modules,
    IReadOnlyList<string> Errors,
    ValuesSample? Values = null,
    ParrySample? Parry = null,
    BossSample? Boss = null,
    string? SuiteVersion = null,
    PlayerSample? Player = null)
{
    public bool IsPlaying => Lifecycle == "playing";

    public const int SupportedSchemaVersion = 1;

    // Returns null for lines that are not valid JSON or not schema v1; callers skip them.
    public static Snapshot? TryParse(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object || Int(r, "schemaVersion") != SupportedSchemaVersion) return null;
            var session = Str(r, "sessionId");
            var sequence = Long(r, "sequence");
            var monotonic = Long(r, "monotonicMs");
            if (session is null || sequence is null || monotonic is null) return null;

            var health = Obj(r, "health");
            var combat = Obj(r, "combat");
            var flask = Obj(r, "flask");
            var modules = Obj(r, "modules");
            var values = Obj(r, "values");
            var errors = new List<string>();
            if (r.TryGetProperty("errors", out var e) && e.ValueKind == JsonValueKind.Array)
                foreach (var item in e.EnumerateArray()) errors.Add(item.ToString());

            return new Snapshot(
                SupportedSchemaVersion,
                session,
                sequence.Value,
                monotonic.Value,
                Long(r, "wallTime"),
                Str(r, "lifecycle") ?? "unknown",
                Int(r, "playerGeneration") ?? 0,
                new HealthSample(Dbl(health, "current"), Dbl(health, "max"), Bool(health, "valid")),
                new CombatSample(Int(combat, "inCombat"), Int(combat, "dead"), Bool(combat, "valid")),
                new FlaskSample(Slot(flask, "top"), Slot(flask, "bottom"), Int(flask, "total"), Bool(flask, "valid")),
                Int(r, "configRevision") ?? 0,
                new ModulesState(Bool(modules, "telemetry"), Bool(modules, "parry"), Bool(modules, "items"), Bool(modules, "lock")),
                errors,
                new ValuesSample(Pair(values, "shachi"), Pair(values, "ultimate"), Pair(values, "spirit")),
                Obj(r, "parry") is { } pr ? new ParrySample(Int(pr, "deflects") ?? 0, Int(pr, "dodges") ?? 0, Int(pr, "hitsTaken") ?? 0, Int(pr, "clean") ?? 0) : null,
                Obj(r, "boss") is { } bo ? new BossSample(Bool(bo, "active"), Int(bo, "count") ?? 0, Str(bo, "id"), Str(bo, "name"), Dbl(bo, "current"), Dbl(bo, "max"), Bool(bo, "valid")) : null,
                Str(r, "suiteVersion"),
                Obj(r, "player") is { } pl ? ParsePlayer(pl) : null);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static PlayerSample ParsePlayer(JsonElement pl)
    {
        var pos = Obj(pl, "position");
        var results = new List<PlayerOpResult>();
        if (pl.TryGetProperty("results", out var res) && res.ValueKind == JsonValueKind.Array)
            foreach (var x in res.EnumerateArray())
                if (Long(x, "seq") is { } seq)
                {
                    var from = Obj(x, "from");
                    results.Add(new PlayerOpResult(seq, Str(x, "kind"), Bool(x, "ok"), Str(x, "error"),
                        Dbl(from, "x"), Dbl(from, "y"), Dbl(from, "z"), Dbl(x, "before"), Dbl(x, "after")));
                }
        return new PlayerSample(Bool(pl, "valid"), Str(pl, "world"), Dbl(pos, "x"), Dbl(pos, "y"), Dbl(pos, "z"),
            Dbl(pl, "xp"), Dbl(pl, "level"), Dbl(pl, "gameSpeed"), Bool(pl, "fly"), results);
    }

    private static HealthSample Pair(JsonElement? parent, string name)
    {
        var o = Obj(parent, name);
        return new HealthSample(Dbl(o, "current"), Dbl(o, "max"), Bool(o, "valid"));
    }

    private static FlaskSlot? Slot(JsonElement? parent, string name)
    {
        var o = Obj(parent, name);
        return o is null ? null : new FlaskSlot(Str(o, "id"), Int(o, "count"), Bool(o, "valid"));
    }

    private static JsonElement? Obj(JsonElement? parent, string name) =>
        parent is { } p && p.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Object ? v : null;

    private static string? Str(JsonElement? parent, string name) =>
        parent is { } p && p.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static double? Dbl(JsonElement? parent, string name) =>
        parent is { } p && p.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

    private static long? Long(JsonElement? parent, string name) =>
        Dbl(parent, name) is { } d ? (long)d : null;

    private static int? Int(JsonElement? parent, string name) =>
        Dbl(parent, name) is { } d ? (int)Math.Round(d) : null;

    private static bool Bool(JsonElement? parent, string name) =>
        parent is { } p && p.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
}
