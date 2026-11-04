using System.Text.Json;
using System.Text.Json.Serialization;

namespace PBZ.Core.Music;

// Manager-only settings (%LOCALAPPDATA%\PBZManager\settings.json). Audio files stay where the user keeps them;
// only their paths are stored (ADR 0001: no audio is copied into the project or the package).
public sealed record MusicSettings(bool Enabled, double Volume, int FadeOutMs, List<PlayRule>? Rules)
{
    // Fields of the T4d format, read once and turned into rules.
    public string? HalfHealthPath { get; init; }
    public string? FlasksEmptyPath { get; init; }

    public static MusicSettings Default => new(false, 0.6, 2000, DefaultRules(null, null));

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    // The two T4d rules (product rules 2–4, 8) as condition-player rules.
    public static List<PlayRule> DefaultRules(string? halfHealthPath, string? flasksEmptyPath) =>
    [
        new("half50", "半血", true, RuleTrigger.HealthBelow, 50, halfHealthPath, 1, RuleScope.OncePerBattle, RuleTrigger.BattleOver),
        new("flasks0", "药喝空", true, RuleTrigger.FlasksEmpty, 0, flasksEmptyPath, 2, RuleScope.OncePerBattle, RuleTrigger.BattleOver,
            Conditions: new RuleConditions(BattleFilter.InBattle)),
    ];

    public static MusicSettings Load(string file)
    {
        try
        {
            if (!File.Exists(file)) return Default;
            var s = JsonSerializer.Deserialize<MusicSettings>(File.ReadAllText(file), Json) ?? Default;
            var rules = (s.Rules ?? DefaultRules(s.HalfHealthPath, s.FlasksEmptyPath)).Select(Migrate).ToList();
            return s with
            {
                Volume = Math.Clamp(s.Volume, 0, 1),
                FadeOutMs = Math.Clamp(s.FadeOutMs, 0, 10_000),
                Rules = rules,
                HalfHealthPath = null,
                FlasksEmptyPath = null,
            };
        }
        catch (JsonException)
        {
            return Default;
        }
    }

    // T7 rules carry FadeOnBattleEnd instead of a fade trigger; false meant "play to the end".
    // The T7 default "flasks empty" rule also played when out of battle; the user asked for it to need a battle
    // (2026-11-04), so a T7-format copy of it gets that condition.
    private static PlayRule Migrate(PlayRule r)
    {
        if (r.FadeOnBattleEnd is not { } fade) return r;
        var next = r with { StopTrigger = fade ? RuleTrigger.BattleOver : null, FadeOnBattleEnd = null };
        if (r.Id == "flasks0" && r.Conditions is null) next = next with { Conditions = new RuleConditions(BattleFilter.InBattle) };
        return next;
    }

    public void Save(string file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var tmp = file + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
        if (File.Exists(file)) File.Replace(tmp, file, null);
        else File.Move(tmp, file);
    }
}
