using System.Text.Json.Serialization;
using PBZ.Core.Events;

namespace PBZ.Core.Music;

// Conditions a rule can start or fade on. Values are stored by name in settings.json, so order may change freely.
public enum RuleTrigger
{
    BattleStarted,
    BattleEnded,     // left combat (5 s without enemies)
    BattleOver,      // a battle ended for any reason: left combat, death, load, game restart
    HealthBelow,
    HealthAbove,
    BigHit,
    FlaskUsed,
    FlasksEmpty,
    FlasksRefilled,
    Death,
    Respawn,
    EnemiesAtLeast,
    BattleLong,
    ShachiFull,
    UltimateReady,
    UltimateUsed,
    SpiritFull,
    SpiritEmpty,
    BossFightStarted,
    BossFightEnded,
    BossHealthBelow,
    BossDefeated,
}

public enum RuleScope
{
    OncePerBattle,
    EveryTime,
}

public enum BattleFilter
{
    Any,
    InBattle,
    OutOfBattle,
}

public enum BossFilter
{
    Any,
    InBossFight,
    NoBossFight,
}

// Extra state checks that must all hold when the start trigger fires (null = not checked). Only the start trigger
// is filtered; fading is not. New checks can be added as further nullable fields without breaking saved rules.
public sealed record RuleConditions(
    BattleFilter Battle = BattleFilter.Any,
    double? HealthBelowPercent = null,
    double? HealthAbovePercent = null,
    int? EnemiesAtLeast = null,
    int? FlasksAtMost = null,
    BossFilter Boss = BossFilter.Any,
    double? BossHealthBelowPercent = null)
{
    public static RuleConditions None => new();

    [JsonIgnore] public bool IsEmpty => this == None;

    public bool Holds(RuleState s) =>
        (Battle == BattleFilter.Any || (Battle == BattleFilter.InBattle) == s.InBattle)
        && (HealthBelowPercent is not { } hb || s.HealthRatio is { } r1 && r1 * 100 < hb)
        && (HealthAbovePercent is not { } ha || s.HealthRatio is { } r2 && r2 * 100 > ha)
        && (EnemiesAtLeast is not { } en || s.Enemies is { } e && e >= en)
        && (FlasksAtMost is not { } fl || s.FlaskTotal is { } f && f <= fl)
        && (Boss == BossFilter.Any || (Boss == BossFilter.InBossFight) == s.BossActive)
        && (BossHealthBelowPercent is not { } bb || s.BossActive && s.BossRatio is { } br && br * 100 < bb);

    public string Summary()
    {
        var parts = new List<string>();
        if (Battle == BattleFilter.InBattle) parts.Add("战斗中");
        if (Battle == BattleFilter.OutOfBattle) parts.Add("非战斗");
        if (HealthBelowPercent is { } hb) parts.Add($"血量<{hb:0.#}%");
        if (HealthAbovePercent is { } ha) parts.Add($"血量>{ha:0.#}%");
        if (EnemiesAtLeast is { } en) parts.Add($"敌人≥{en}");
        if (FlasksAtMost is { } fl) parts.Add($"药≤{fl}");
        if (Boss == BossFilter.InBossFight) parts.Add("Boss 战中");
        if (Boss == BossFilter.NoBossFight) parts.Add("非 Boss 战");
        if (BossHealthBelowPercent is { } bb) parts.Add($"Boss 血量<{bb:0.#}%");
        return parts.Count == 0 ? "无" : string.Join("，", parts);
    }
}

// Game state at the sample the events came from, for RuleConditions. Unknown values fail any check that needs them.
public sealed record RuleState(bool InBattle, double? HealthRatio, int? Enemies, int? FlaskTotal, bool BossActive = false, double? BossRatio = null)
{
    public static RuleState Unknown => new(false, null, null, null);
}

public enum TriggerParam
{
    None,
    Percent, // of max health
    Count,   // enemies
    Seconds,
}

public sealed record TriggerInfo(RuleTrigger Trigger, string Name, TriggerParam Param, double DefaultValue, double Min, double Max, string Help);

// Names, parameter meaning and limits of every trigger, shared by the rule layer and the window.
public static class RuleTriggers
{
    public static readonly IReadOnlyList<TriggerInfo> All =
    [
        new(RuleTrigger.BattleStarted, "开战", TriggerParam.None, 0, 0, 0, "连续两次采样有敌人交战"),
        new(RuleTrigger.BattleEnded, "脱战", TriggerParam.None, 0, 0, 0, "连续 5 秒无敌人交战"),
        new(RuleTrigger.BattleOver, "战斗结束（任何原因）", TriggerParam.None, 0, 0, 0, "脱战、阵亡、读图、游戏重启都算"),
        new(RuleTrigger.HealthBelow, "血量跌破", TriggerParam.Percent, 50, 1, 99, "战斗中血量从上方跌到参数 % 及以下"),
        new(RuleTrigger.HealthAbove, "血量回升到", TriggerParam.Percent, 80, 1, 100, "血量从下方回到参数 % 及以上（战斗内外都算）"),
        new(RuleTrigger.BigHit, "一次掉血超过", TriggerParam.Percent, 25, 1, 99, "两次采样之间掉血达到上限的参数 %"),
        new(RuleTrigger.FlaskUsed, "喝药", TriggerParam.None, 0, 0, 0, "两个药槽剩余次数合计减少（换药也可能触发）"),
        new(RuleTrigger.FlasksEmpty, "药喝空", TriggerParam.None, 0, 0, 0, "两个药槽剩余次数合计变为 0"),
        new(RuleTrigger.FlasksRefilled, "补药", TriggerParam.None, 0, 0, 0, "药槽次数增加（摇铃、拾取）"),
        new(RuleTrigger.Death, "阵亡", TriggerParam.None, 0, 0, 0, "Dead 标签或血量归零（未实战验证）"),
        new(RuleTrigger.Respawn, "重生/读图", TriggerParam.None, 0, 0, 0, "出现新的角色（首次进存档不算）"),
        new(RuleTrigger.EnemiesAtLeast, "被围攻（敌人数达到）", TriggerParam.Count, 3, 1, 20, "战斗中交战计数升到参数及以上"),
        new(RuleTrigger.BattleLong, "战斗持续超过", TriggerParam.Seconds, 60, 1, 3600, "同一场战斗持续参数秒"),
        new(RuleTrigger.ShachiFull, "杀气满", TriggerParam.None, 0, 0, 0, "杀气回到上限（锁定开着时不会再触发）"),
        new(RuleTrigger.UltimateReady, "绝技能量满", TriggerParam.None, 0, 0, 0, "绝技能量攒满"),
        new(RuleTrigger.UltimateUsed, "放出绝技", TriggerParam.None, 0, 0, 0, "绝技能量从满跌到一半以下"),
        new(RuleTrigger.SpiritFull, "影/魂点满", TriggerParam.None, 0, 0, 0, "影/魂点数回到上限"),
        new(RuleTrigger.SpiritEmpty, "影/魂点用完", TriggerParam.None, 0, 0, 0, "影/魂点数变为 0"),
        new(RuleTrigger.BossFightStarted, "Boss 战开始", TriggerParam.None, 0, 0, 0, "屏幕上出现 Boss 血条"),
        new(RuleTrigger.BossFightEnded, "Boss 战结束", TriggerParam.None, 0, 0, 0, "Boss 血条消失（击败、阵亡、读档都算）"),
        new(RuleTrigger.BossHealthBelow, "Boss 血量跌破", TriggerParam.Percent, 50, 1, 99, "Boss 血量从上方跌到参数 % 及以下"),
        new(RuleTrigger.BossDefeated, "击败 Boss", TriggerParam.None, 0, 0, 0, "Boss 血量归零（每场一次）"),
    ];

    public static TriggerInfo Info(RuleTrigger t) => All.First(i => i.Trigger == t);

    public static double Clamp(RuleTrigger t, double value)
    {
        var i = Info(t);
        return i.Param == TriggerParam.None ? value : Math.Clamp(value, i.Min, i.Max);
    }

    // The engine threshold a rule parameter maps to: percentages become ratios, counts and seconds stay as they are.
    internal static double EngineThreshold(RuleTrigger t, double value)
    {
        var v = Clamp(t, value);
        return Info(t).Param == TriggerParam.Percent ? Math.Round(v / 100, 4) : v;
    }

    internal static EngineEventKind? Kind(RuleTrigger t) => t switch
    {
        RuleTrigger.BattleStarted => EngineEventKind.BattleStarted,
        RuleTrigger.BattleEnded or RuleTrigger.BattleOver => EngineEventKind.BattleEnded,
        RuleTrigger.HealthBelow => EngineEventKind.HealthBelow,
        RuleTrigger.HealthAbove => EngineEventKind.HealthAbove,
        RuleTrigger.BigHit => EngineEventKind.BigHit,
        RuleTrigger.FlaskUsed => EngineEventKind.FlaskUsed,
        RuleTrigger.FlasksEmpty => EngineEventKind.FlasksEmpty,
        RuleTrigger.FlasksRefilled => EngineEventKind.FlasksRefilled,
        RuleTrigger.Death => EngineEventKind.Death,
        RuleTrigger.Respawn => EngineEventKind.Respawn,
        RuleTrigger.EnemiesAtLeast => EngineEventKind.EnemiesAtLeast,
        RuleTrigger.BattleLong => EngineEventKind.BattleLong,
        RuleTrigger.ShachiFull => EngineEventKind.ShachiFull,
        RuleTrigger.UltimateReady => EngineEventKind.UltimateReady,
        RuleTrigger.UltimateUsed => EngineEventKind.UltimateUsed,
        RuleTrigger.SpiritFull => EngineEventKind.SpiritFull,
        RuleTrigger.SpiritEmpty => EngineEventKind.SpiritEmpty,
        RuleTrigger.BossFightStarted => EngineEventKind.BossFightStarted,
        RuleTrigger.BossFightEnded => EngineEventKind.BossFightEnded,
        RuleTrigger.BossHealthBelow => EngineEventKind.BossHealthBelow,
        RuleTrigger.BossDefeated => EngineEventKind.BossDefeated,
        _ => null,
    };

    public static bool Matches(RuleTrigger t, double param, EngineEvent e)
    {
        if (Kind(t) != e.Kind) return false;
        if (t == RuleTrigger.BattleEnded && e.Reason != "left combat") return false;
        if (Info(t).Param == TriggerParam.None) return true;
        return e.Threshold is { } th && Math.Abs(th - EngineThreshold(t, param)) < 1e-4;
    }
}

// One user rule of the condition player (ADR 0002). ThresholdPercent is the start trigger's parameter (percent,
// enemy count or seconds, see RuleTriggers); StopTrigger fades the song out, null = let it play to the end.
public sealed record PlayRule(
    string Id,
    string Name,
    bool Enabled,
    RuleTrigger Trigger,
    double ThresholdPercent,
    string? TrackPath,
    int Priority,
    RuleScope Scope,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] RuleTrigger? StopTrigger = RuleTrigger.BattleOver,
    double StopThreshold = 80,
    RuleConditions? Conditions = null,
    List<string>? Tracks = null,
    RulePlayback? Playback = null)
{
    // All songs of the rule; older rules only have TrackPath.
    [JsonIgnore] public IReadOnlyList<string> AllTracks =>
        Tracks is { Count: > 0 } t ? t : string.IsNullOrWhiteSpace(TrackPath) ? [] : [TrackPath];

    [JsonIgnore] public RulePlayback How => Playback ?? RulePlayback.Default;

    [JsonIgnore] public RuleConditions When => Conditions ?? RuleConditions.None;

    // T7 format: "fade when the battle ends". Read once by MusicSettings.Load and turned into StopTrigger.
    public bool? FadeOnBattleEnd { get; init; }

    public static PlayRule New(RuleTrigger trigger) =>
        new(Guid.NewGuid().ToString("N")[..8], "新规则", true, trigger, RuleTriggers.Info(trigger).DefaultValue, null, 1, RuleScope.OncePerBattle);
}

public enum LoopMode
{
    Auto, // loop when the rule has a fade trigger, otherwise play once
    Loop,
    Once,
}

// Per-rule playback options. FadeOutSeconds null = the global fade-out time.
public sealed record RulePlayback(
    int VolumePercent = 100,
    double StartSeconds = 0,
    LoopMode Loop = LoopMode.Auto,
    double FadeInSeconds = 0.3,
    double? FadeOutSeconds = null,
    double CooldownSeconds = 0,
    bool Shuffle = true)
{
    public static RulePlayback Default => new();

    [JsonIgnore] public bool IsDefault => this == Default;

    public RulePlayback Clamped() => this with
    {
        VolumePercent = Math.Clamp(VolumePercent, 0, 200),
        StartSeconds = Math.Clamp(StartSeconds, 0, 3600),
        FadeInSeconds = Math.Clamp(FadeInSeconds, 0, 30),
        FadeOutSeconds = FadeOutSeconds is { } f ? Math.Clamp(f, 0, 30) : null,
        CooldownSeconds = Math.Clamp(CooldownSeconds, 0, 3600),
    };

    public string Summary(int trackCount)
    {
        var parts = new List<string>();
        if (trackCount > 1) parts.Add(Shuffle ? $"{trackCount} 首随机" : $"{trackCount} 首轮流");
        if (VolumePercent != 100) parts.Add($"音量 {VolumePercent}%");
        if (StartSeconds > 0) parts.Add($"从 {StartSeconds:0.#} 秒起");
        if (Loop == LoopMode.Loop) parts.Add("循环");
        if (Loop == LoopMode.Once) parts.Add("放一遍");
        if (Math.Abs(FadeInSeconds - 0.3) > 1e-9) parts.Add($"淡入 {FadeInSeconds:0.#}s");
        if (FadeOutSeconds is { } fo) parts.Add($"淡出 {fo:0.#}s");
        if (CooldownSeconds > 0) parts.Add($"冷却 {CooldownSeconds:0.#}s");
        return parts.Count == 0 ? "默认" : string.Join("，", parts);
    }
}

// One playback order for the audio output.
public sealed record PlayRequest(string Path, bool Loop, double VolumeFactor = 1, double StartSeconds = 0, double FadeInSeconds = 0.3, double? FadeOutSeconds = null);

// What actually makes sound. The WPF implementation lives in the manager; tests use a fake.
public interface IAudioOutput
{
    // The key passed to the last Play, or null once it has finished or is fading out.
    string? CurrentKey { get; }
    void Play(string key, PlayRequest request);
    void FadeOut();
}

// Applies the rule table to the events of one telemetry sample.
public sealed class ConditionPlayer
{
    private readonly IAudioOutput _out;
    private readonly HashSet<(string rule, int battle)> _fired = [];
    private readonly Dictionary<string, long> _lastStartMs = [];
    private readonly Dictionary<string, int> _nextTrack = [];
    private readonly Dictionary<string, string> _lastTrack = [];
    private readonly Random _random;
    private PlayRule? _playing;

    public ConditionPlayer(IAudioOutput output, Random? random = null)
    {
        _out = output;
        _random = random ?? new Random();
    }

    public bool Enabled { get; set; }
    public IReadOnlyList<PlayRule> Rules { get; set; } = [];

    public event Action<PlayRule, string>? Problem;

    // Thresholds the engine should watch, from the enabled rules' start and fade triggers.
    public IReadOnlyDictionary<EngineEventKind, IReadOnlyCollection<double>> Watch
    {
        get
        {
            var map = new Dictionary<EngineEventKind, HashSet<double>>();
            void Add(RuleTrigger t, double p)
            {
                if (RuleTriggers.Info(t).Param == TriggerParam.None || RuleTriggers.Kind(t) is not { } k) return;
                if (!map.TryGetValue(k, out var set)) map[k] = set = [];
                set.Add(RuleTriggers.EngineThreshold(t, p));
            }
            foreach (var r in Rules.Where(r => r.Enabled))
            {
                Add(r.Trigger, r.ThresholdPercent);
                if (r.StopTrigger is { } st) Add(st, r.StopThreshold);
            }
            return map.ToDictionary(kv => kv.Key, kv => (IReadOnlyCollection<double>)kv.Value.ToList());
        }
    }

    // Kept for tests and callers that only care about HealthBelow.
    public IReadOnlyCollection<double> HealthThresholds =>
        Watch.TryGetValue(EngineEventKind.HealthBelow, out var t) ? t : [];

    public void Handle(IReadOnlyList<EngineEvent> sampleEvents) => Handle(sampleEvents, null);

    // state: the game state of the sample, for rules with conditions. Null skips condition checks (old callers).
    public void Handle(IReadOnlyList<EngineEvent> sampleEvents, RuleState? state)
    {
        if (!Enabled || sampleEvents.Count == 0) return;
        var playing = _out.CurrentKey is { } key ? _playing is { } p && p.Id == key ? p : null : null;

        // Fade first, so a rule triggered by the same sample (death, leaving combat) can start fresh.
        if (playing is { StopTrigger: { } stop } && sampleEvents.Any(e => RuleTriggers.Matches(stop, playing.StopThreshold, e)))
        {
            _out.FadeOut();
            playing = null;
        }

        var candidates = new List<(PlayRule rule, int order)>();
        for (var i = 0; i < Rules.Count; i++)
        {
            var rule = Rules[i];
            if (!rule.Enabled) continue;
            var hit = sampleEvents.FirstOrDefault(e => RuleTriggers.Matches(rule.Trigger, rule.ThresholdPercent, e));
            if (hit is null) continue;
            if (state is not null && !rule.When.Holds(state)) continue;
            // Cooldown in game time, measured from the rule's last start.
            if (rule.How.CooldownSeconds > 0 && _lastStartMs.TryGetValue(rule.Id, out var last)
                && hit.MonotonicMs - last < rule.How.CooldownSeconds * 1000) continue;
            if (rule.Scope == RuleScope.OncePerBattle && hit.BattleId > 0 && !_fired.Add((rule.Id, hit.BattleId))) continue;
            candidates.Add((rule, i));
        }
        if (candidates.Count == 0) return;

        // Rule 4 generalised: the highest priority wins; the others in the same sample are used up.
        var winner = candidates.OrderByDescending(c => c.rule.Priority).ThenBy(c => c.order).First().rule;
        // A song that is playing is only interrupted by a strictly higher priority (user, 2026-11-07).
        if (playing is not null && playing.Priority >= winner.Priority) return;
        var track = PickTrack(winner);
        if (track is null)
        {
            Problem?.Invoke(winner, "没有选择歌曲");
            return;
        }
        _playing = winner;
        _lastStartMs[winner.Id] = sampleEvents[^1].MonotonicMs;
        _out.Play(winner.Id, Request(winner, track));
    }

    // The request for one track of a rule. Auto loop: rules with a fade trigger loop until they fade, others play once.
    public static PlayRequest Request(PlayRule rule, string track)
    {
        var how = rule.How.Clamped();
        var loop = how.Loop switch
        {
            LoopMode.Loop => true,
            LoopMode.Once => false,
            _ => rule.StopTrigger is not null,
        };
        return new PlayRequest(track, loop, how.VolumePercent / 100.0, how.StartSeconds, how.FadeInSeconds, how.FadeOutSeconds);
    }

    // Shuffle avoids playing the same song twice in a row; otherwise the songs take turns.
    private string? PickTrack(PlayRule rule)
    {
        var tracks = rule.AllTracks.Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
        if (tracks.Count == 0) return null;
        string pick;
        if (tracks.Count == 1) pick = tracks[0];
        else if (rule.How.Shuffle)
        {
            var choices = _lastTrack.TryGetValue(rule.Id, out var prev) ? tracks.Where(t => t != prev).ToList() : tracks;
            pick = choices[_random.Next(choices.Count)];
        }
        else
        {
            var i = _nextTrack.GetValueOrDefault(rule.Id) % tracks.Count;
            pick = tracks[i];
            _nextTrack[rule.Id] = i + 1;
        }
        _lastTrack[rule.Id] = pick;
        return pick;
    }
}
