using System.Text.Json;
using PBZ.Core.Suite;

namespace PBZ.Core.Player;

// Continuous player settings for PBZSuite 1.0.3 (config.json "cheats"). Neutral values (1, false) mean off.
// GameSpeed: global time dilation. MoveSpeed: jog / sprint / walk / crouch speed. Damage: Attack and ShachiAttack.
// Taken: the game's damage-taken multipliers (0 = no damage). Medicine: keep the equipped medicines full. Fly: fly
// with collision off.
public sealed record SuiteCheats(double GameSpeed = 1, double MoveSpeed = 1, double Damage = 1, double Taken = 1,
    bool Medicine = false, bool Fly = false)
{
    public static readonly (double Min, double Max) GameSpeedRange = (0.1, 5), MoveSpeedRange = (0.5, 5),
        DamageRange = (0.1, 100), TakenRange = (0, 5);

    public static SuiteCheats Neutral => new();

    public bool AnyActive => this != Neutral;

    public SuiteCheats Clamped() => new(
        Math.Clamp(GameSpeed, GameSpeedRange.Min, GameSpeedRange.Max),
        Math.Clamp(MoveSpeed, MoveSpeedRange.Min, MoveSpeedRange.Max),
        Math.Clamp(Damage, DamageRange.Min, DamageRange.Max),
        Math.Clamp(Taken, TakenRange.Min, TakenRange.Max),
        Medicine, Fly);
}

// One-shot player action: "teleport" (X, Y, Z in cm), "nudge" (X = forward cm, Z = up cm), "xp" / "level" (Value).
public sealed record PlayerOp(long Seq, string Kind, double? X = null, double? Y = null, double? Z = null, double? Value = null);

public static class PlayerEdits
{
    public static readonly IReadOnlySet<string> Kinds = new HashSet<string> { "teleport", "nudge", "xp", "level" };

    public static SuiteConfig Append(SuiteConfig config, PlayerOp op)
    {
        if (!Kinds.Contains(op.Kind)) throw new ArgumentException("unknown player op " + op.Kind);
        var ops = new List<PlayerOp>(config.PlayerOps ?? []);
        var next = ops.Count == 0 ? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() : ops.Max(o => o.Seq) + 1;
        ops.Add(op with { Seq = next });
        if (ops.Count > 30) ops = ops.Skip(ops.Count - 30).ToList();
        return config with { PlayerOps = ops };
    }
}

// A saved place. World is the map name the game reports; teleporting only makes sense within the same map.
public sealed record Waypoint(string Name, string World, double X, double Y, double Z)
{
    public string Where => $"{World}  ({X / 100:0}, {Y / 100:0}, {Z / 100:0}) 米";
}

public static class WaypointStore
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    public static List<Waypoint> Load(string file)
    {
        try
        {
            return File.Exists(file) ? JsonSerializer.Deserialize<List<Waypoint>>(File.ReadAllText(file), Json) ?? [] : [];
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public static void Save(string file, IEnumerable<Waypoint> points)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var tmp = file + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(points.ToList(), Json));
        File.Move(tmp, file, overwrite: true);
    }
}
