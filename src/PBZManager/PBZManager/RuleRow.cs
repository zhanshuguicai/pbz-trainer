using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using PBZ.Core.Music;

namespace PBZManager;

public sealed record Choice<T>(T Value, string Name)
{
    public override string ToString() => Name;
}

// Editable row of the condition-player table; converts to and from the immutable PlayRule.
public sealed class RuleRow : INotifyPropertyChanged
{
    public static readonly IReadOnlyList<Choice<RuleTrigger>> Triggers =
        RuleTriggers.All.Select(i => new Choice<RuleTrigger>(i.Trigger, i.Name)).ToList();

    public static readonly IReadOnlyList<Choice<RuleTrigger?>> StopTriggers =
        [new(null, "不淡出（放完）"), .. RuleTriggers.All.Select(i => new Choice<RuleTrigger?>(i.Trigger, i.Name))];

    public static readonly IReadOnlyList<Choice<RuleScope>> Scopes =
    [
        new(RuleScope.OncePerBattle, "每场一次"),
        new(RuleScope.EveryTime, "每次"),
    ];

    private readonly string _id;
    private bool _enabled;
    private string _name;
    private RuleTrigger _trigger;
    private double _threshold;
    private List<string> _tracks;
    private RulePlayback _playback;
    private int _priority;
    private RuleScope _scope;
    private RuleTrigger? _stop;
    private double _stopThreshold;
    private RuleConditions _conditions;

    public RuleRow(PlayRule r)
    {
        _id = r.Id;
        _enabled = r.Enabled;
        _name = r.Name;
        _trigger = r.Trigger;
        _threshold = RuleTriggers.Clamp(r.Trigger, r.ThresholdPercent);
        _tracks = r.AllTracks.ToList();
        _playback = r.How;
        _priority = r.Priority;
        _scope = r.Scope;
        _stop = r.StopTrigger;
        _stopThreshold = r.StopTrigger is { } st ? RuleTriggers.Clamp(st, r.StopThreshold) : r.StopThreshold;
        _conditions = r.When;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id => _id;
    public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }
    public string Name { get => _name; set => Set(ref _name, value); }

    public RuleTrigger Trigger
    {
        get => _trigger;
        set
        {
            var oldParam = RuleTriggers.Info(_trigger).Param;
            if (!Set(ref _trigger, value)) return;
            // A different kind of parameter (percent → seconds…) starts from that trigger's default.
            if (RuleTriggers.Info(value).Param != oldParam) ThresholdPercent = RuleTriggers.Info(value).DefaultValue;
            Notify(nameof(UsesThreshold));
            Notify(nameof(ParamUnit));
            Notify(nameof(TriggerHelp));
        }
    }

    public double ThresholdPercent { get => _threshold; set => Set(ref _threshold, RuleTriggers.Clamp(_trigger, value)); }
    public bool UsesThreshold => RuleTriggers.Info(_trigger).Param != TriggerParam.None;
    public string ParamUnit => Unit(_trigger);
    public string TriggerHelp => RuleTriggers.Info(_trigger).Help;

    public RuleTrigger? StopTrigger
    {
        get => _stop;
        set
        {
            var oldParam = _stop is { } o ? RuleTriggers.Info(o).Param : TriggerParam.None;
            if (!Set(ref _stop, value)) return;
            if (value is { } v && RuleTriggers.Info(v).Param != oldParam) StopThreshold = RuleTriggers.Info(v).DefaultValue;
            Notify(nameof(StopUsesThreshold));
            Notify(nameof(StopUnit));
        }
    }

    public double StopThreshold { get => _stopThreshold; set => Set(ref _stopThreshold, _stop is { } st ? RuleTriggers.Clamp(st, value) : value); }
    public bool StopUsesThreshold => _stop is { } st && RuleTriggers.Info(st).Param != TriggerParam.None;
    public string StopUnit => _stop is { } st ? Unit(st) : "";

    // First song (preview, older settings); Tracks holds all of them.
    public string? TrackPath
    {
        get => _tracks.FirstOrDefault();
        set => Tracks = string.IsNullOrWhiteSpace(value) ? [] : [value];
    }

    public IReadOnlyList<string> Tracks
    {
        get => _tracks;
        set
        {
            _tracks = value.Where(t => !string.IsNullOrWhiteSpace(t)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            Notify(nameof(Tracks));
            Notify(nameof(TrackPath));
            Notify(nameof(TrackName));
            Notify(nameof(TrackTip));
            Notify(nameof(PlaybackSummary));
        }
    }

    public RulePlayback Playback { get => _playback; set { if (Set(ref _playback, value.Clamped())) Notify(nameof(PlaybackSummary)); } }
    public string PlaybackSummary => _playback.Summary(_tracks.Count);
    public string TrackTip => string.Join(Environment.NewLine, _tracks);
    public int Priority { get => _priority; set => Set(ref _priority, Math.Clamp(value, 0, 99)); }
    public RuleScope Scope { get => _scope; set => Set(ref _scope, value); }

    public RuleConditions Conditions { get => _conditions; set { if (Set(ref _conditions, value)) Notify(nameof(ConditionsSummary)); } }
    public string ConditionsSummary => _conditions.Summary();

    public string TrackName => _tracks.Count switch
    {
        0 => "（未选择）",
        1 => Path.GetFileName(_tracks[0]),
        _ => $"{Path.GetFileName(_tracks[0])} 等 {_tracks.Count} 首",
    };

    public PlayRule ToRule() => new(_id, _name, _enabled, _trigger, _threshold, _tracks.FirstOrDefault(), _priority, _scope, _stop, _stopThreshold,
        _conditions.IsEmpty ? null : _conditions, _tracks.Count > 1 ? [.. _tracks] : null, _playback.IsDefault ? null : _playback);

    private static string Unit(RuleTrigger t) => RuleTriggers.Info(t).Param switch
    {
        TriggerParam.Percent => "%",
        TriggerParam.Count => "个",
        TriggerParam.Seconds => "秒",
        _ => "",
    };

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Notify(name!);
        return true;
    }

    private void Notify(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
