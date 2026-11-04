using PBZ.Core.Telemetry;

namespace PBZ.Core.Events;

public enum EngineEventKind
{
    BattleStarted,
    BattleEnded,
    HealthBelow,   // health ratio crossed down through Threshold inside a battle
    FlasksEmpty,   // the two equipped slots' total went from positive to 0
    Death,
    Respawn,       // a new player pawn after a previous one (load, respawn); not the first one seen
    HealthAbove,   // health ratio crossed up to or above Threshold (any time; e.g. healed back)
    BigHit,        // health dropped by at least Threshold (ratio of max) between two samples
    FlaskUsed,     // the two slots' total went down (drank); also fires on the drink that empties them
    FlasksRefilled,// the two slots' total went up (bell rest, pickup)
    EnemiesAtLeast,// inside a battle, the combat count rose to at least Threshold
    BattleLong,    // inside a battle, the battle has lasted Threshold seconds
    ShachiFull,
    UltimateReady, // ultimate charge reached its max
    UltimateUsed,  // ultimate charge fell from full to below half
    SpiritFull,
    SpiritEmpty,
    BossFightStarted, // a boss health bar appeared (Reason = boss name)
    BossFightEnded,   // the bar is gone (Reason = "defeated" or "gone")
    BossHealthBelow,  // the boss's health ratio crossed down through Threshold
    BossDefeated,     // the boss's health reached 0 (once per fight)
}

public sealed record EngineEvent(EngineEventKind Kind, int BattleId, long Sequence, long MonotonicMs, string Reason, double? Threshold = null);

public sealed class EventEngineOptions
{
    // Consecutive valid samples with InCombat > 0 before a battle starts.
    public int EnterSamples { get; init; } = 2;

    // InCombat must stay 0 this long (in game time) before the battle ends. T4a showed many sub-second dips and
    // one 4.5 s dip inside the same fight (t=365.6–370.1 s), which 3 s would have split into two battles.
    public long ExitConfirmMs { get; init; } = 5000;

    // A longer gap between samples (loading screens reach 10+ s) breaks any running confirmation.
    public long MaxSampleGapMs { get; init; } = 1000;
}

// Turns telemetry snapshots into atomic game events (ADR 0002). It reports every edge; deciding what plays,
// "once per battle" and priorities belong to the rule layer (Music/ConditionPlayer).
// Pure state machine: no clocks, no I/O, so it can be replayed in tests.
public sealed class EventEngine
{
    private readonly EventEngineOptions _o;

    private string? _session;
    private long _lastSequence = -1;
    private long? _lastMs;
    private int? _generation;
    private bool _skipFirstSampleOfGeneration;

    private int _battleId;
    private bool _inBattle;
    private int _enterRun;
    private long? _zeroSinceMs;
    private bool _deathLatched;

    private double? _prevRatio;
    private int? _prevFlaskTotal;
    private int? _prevEnemies;
    private long _battleStartMs;
    private long? _prevBattleMs;
    private (double? shachi, double? ult, double? spirit) _prevValues;
    private bool _bossActive;
    private string? _bossId;
    private string? _bossName;
    private double? _prevBossRatio;
    private bool _bossDefeated;

    public bool BossActive => _bossActive;
    public string? BossName => _bossName;

    public EventEngine(EventEngineOptions? options = null) => _o = options ?? new EventEngineOptions();

    public bool InBattle => _inBattle;
    public int BattleId => _battleId;

    // Thresholds to report crossings for, per event kind (health kinds: ratio 0–1; EnemiesAtLeast: count;
    // BattleLong: seconds). The rule layer sets these from its enabled rules. Kinds without thresholds ignore it.
    public IReadOnlyDictionary<EngineEventKind, IReadOnlyCollection<double>> Watch { get; set; } =
        new Dictionary<EngineEventKind, IReadOnlyCollection<double>> { [EngineEventKind.HealthBelow] = [0.5] };

    // Kept for callers that only care about HealthBelow.
    public IReadOnlyCollection<double> HealthThresholds
    {
        get => Thresholds(EngineEventKind.HealthBelow);
        set => Watch = new Dictionary<EngineEventKind, IReadOnlyCollection<double>>(Watch) { [EngineEventKind.HealthBelow] = value };
    }

    private IReadOnlyCollection<double> Thresholds(EngineEventKind kind) => Watch.TryGetValue(kind, out var t) ? t : [];

    public IReadOnlyList<EngineEvent> Process(Snapshot s)
    {
        var events = new List<EngineEvent>();

        if (s.SessionId != _session)
        {
            if (_inBattle) EndBattle(s, "session changed", events);
            _session = s.SessionId;
            _lastSequence = -1;
            _lastMs = null;
            _generation = null;
            ResetTracking();
        }
        if (s.Sequence <= _lastSequence) return events; // duplicate or stale line
        var gapTooLong = _lastMs is { } last && s.MonotonicMs - last > _o.MaxSampleGapMs;
        _lastSequence = s.Sequence;
        _lastMs = s.MonotonicMs;

        if (s.PlayerGeneration != _generation)
        {
            // New pawn (load, respawn): its first sample carries base attributes (850/977.5 in T4a), not real values.
            if (_inBattle) EndBattle(s, "player changed", events);
            // Generation 0 means no pawn yet (main menu), so the first real pawn is not a respawn.
            if (_generation is > 0) events.Add(new EngineEvent(EngineEventKind.Respawn, 0, s.Sequence, s.MonotonicMs, "new player pawn"));
            _generation = s.PlayerGeneration;
            ResetTracking();
            _deathLatched = false;
            _skipFirstSampleOfGeneration = true;
        }

        if (!s.IsPlaying)
        {
            // Unknown state never confirms leaving combat and never feeds edge detection.
            ResetTracking();
            return events;
        }

        if (_skipFirstSampleOfGeneration)
        {
            _skipFirstSampleOfGeneration = false;
            UpdateCombat(s, gapTooLong, events);
            _prevRatio = null;
            _prevFlaskTotal = null;
            _prevEnemies = null;
            _prevValues = default;
            return events;
        }

        DetectDeath(s, events);
        var startedNow = UpdateCombat(s, gapTooLong, events);
        UpdateHealth(s, startedNow, events);
        UpdateFlasks(s, events);
        UpdateBattleExtras(s, startedNow, events);
        UpdateValues(s, events);
        UpdateBoss(s, events);
        return events;
    }

    // Boss fights follow the HUD's boss health bar. An unreadable HUD (Valid false while not active) never ends a fight.
    private void UpdateBoss(Snapshot s, List<EngineEvent> events)
    {
        if (s.Boss is not { } b) return;
        var battle = _inBattle ? _battleId : 0;
        void Add(EngineEventKind kind, string reason, double? t = null) => events.Add(new EngineEvent(kind, battle, s.Sequence, s.MonotonicMs, reason, t));

        if (b.Active && (!_bossActive || b.Id != _bossId))
        {
            if (_bossActive) EndBoss(Add);
            _bossActive = true;
            _bossId = b.Id;
            _bossName = b.Name;
            _prevBossRatio = b.Ratio;
            _bossDefeated = false;
            Add(EngineEventKind.BossFightStarted, b.Name ?? "boss");
            return;
        }
        if (b.Active)
        {
            var ratio = b.Ratio;
            var prev = _prevBossRatio;
            if (ratio is { } r)
            {
                if (prev is { } p)
                    foreach (var t in Thresholds(EngineEventKind.BossHealthBelow).OrderByDescending(t => t))
                        if (p > t && r <= t) Add(EngineEventKind.BossHealthBelow, $"{_bossName} crossed {t:P0}", t);
                if (r <= 0.0001 && !_bossDefeated)
                {
                    _bossDefeated = true;
                    Add(EngineEventKind.BossDefeated, _bossName ?? "boss");
                }
                _prevBossRatio = r;
            }
            return;
        }
        if (_bossActive && b.Valid) EndBoss(Add);
    }

    private void EndBoss(Action<EngineEventKind, string, double?> add)
    {
        // A bar that disappears right after the boss was at (almost) no health counts as a win.
        var defeated = _bossDefeated || _prevBossRatio is <= 0.02;
        if (defeated && !_bossDefeated) add(EngineEventKind.BossDefeated, _bossName ?? "boss", null);
        add(EngineEventKind.BossFightEnded, defeated ? "defeated" : "gone", null);
        _bossActive = false;
        _bossId = null;
        _bossName = null;
        _prevBossRatio = null;
        _bossDefeated = false;
    }

    // Death fires once per life: on the Dead tag, or on health reaching 0 if the tag lags. It clears once the
    // player is alive again (tag 0 and health above 0) or a new pawn appears.
    private void DetectDeath(Snapshot s, List<EngineEvent> events)
    {
        var ratio = s.Health.Ratio;
        var tagDead = s.Combat.Valid && s.Combat.Dead > 0;
        if (_deathLatched)
        {
            if (s.Combat.Valid && s.Combat.Dead == 0 && ratio > 0) _deathLatched = false;
            return;
        }
        var healthDead = ratio is <= 0 && _prevRatio is > 0;
        if (!tagDead && !healthDead) return;
        _deathLatched = true;
        events.Add(new EngineEvent(EngineEventKind.Death, _inBattle ? _battleId : 0, s.Sequence, s.MonotonicMs, tagDead ? "dead tag" : "health reached 0"));
        if (_inBattle) EndBattle(s, "death", events);
    }

    private bool UpdateCombat(Snapshot s, bool gapTooLong, List<EngineEvent> events)
    {
        var c = s.Combat;
        if (!c.Valid || c.InCombat is null || c.Dead is null || _deathLatched)
        {
            _enterRun = 0;
            _zeroSinceMs = null;
            return false;
        }
        if (c.InCombat > 0)
        {
            _zeroSinceMs = null;
            if (_inBattle) return false;
            _enterRun = gapTooLong ? 1 : _enterRun + 1;
            if (_enterRun < _o.EnterSamples) return false;
            _battleId++;
            _inBattle = true;
            _battleStartMs = s.MonotonicMs;
            _prevBattleMs = null;
            events.Add(new EngineEvent(EngineEventKind.BattleStarted, _battleId, s.Sequence, s.MonotonicMs, "in combat"));
            return true;
        }
        _enterRun = 0;
        if (!_inBattle) return false;
        if (_zeroSinceMs is null || gapTooLong) _zeroSinceMs = s.MonotonicMs;
        else if (s.MonotonicMs - _zeroSinceMs >= _o.ExitConfirmMs) EndBattle(s, "left combat", events);
        return false;
    }

    // A crossing from above a threshold to at-or-below it, inside a battle. The sample that starts a battle only
    // sets the baseline, so entering a battle already below a threshold never reports it.
    private void UpdateHealth(Snapshot s, bool battleStartedNow, List<EngineEvent> events)
    {
        var ratio = s.Health.Ratio;
        var prev = _prevRatio;
        _prevRatio = ratio;
        if (ratio is null || prev is null) return;
        var battle = _inBattle ? _battleId : 0;
        foreach (var t in Thresholds(EngineEventKind.HealthAbove).OrderBy(t => t))
            if (prev < t && ratio >= t)
                events.Add(new EngineEvent(EngineEventKind.HealthAbove, battle, s.Sequence, s.MonotonicMs, $"health rose to {t:P0}", t));
        foreach (var t in Thresholds(EngineEventKind.BigHit).OrderByDescending(t => t))
            if (prev - ratio >= t && ratio > 0)
            {
                events.Add(new EngineEvent(EngineEventKind.BigHit, battle, s.Sequence, s.MonotonicMs, $"lost {prev - ratio:P0} at once", t));
            }
        if (!_inBattle || battleStartedNow) return;
        foreach (var t in Thresholds(EngineEventKind.HealthBelow).OrderByDescending(t => t))
            if (prev > t && ratio <= t)
                events.Add(new EngineEvent(EngineEventKind.HealthBelow, _battleId, s.Sequence, s.MonotonicMs, $"health crossed {t:P0}", t));
    }

    // Rules 3/3b: starting at 0 or passing through unknown never reports.
    private void UpdateFlasks(Snapshot s, List<EngineEvent> events)
    {
        var total = s.Flask.Valid ? s.Flask.Total : null;
        var prev = _prevFlaskTotal;
        _prevFlaskTotal = total;
        if (prev is not { } before || total is not { } now || before == now) return;
        var battle = _inBattle ? _battleId : 0;
        if (now < before) events.Add(new EngineEvent(EngineEventKind.FlaskUsed, battle, s.Sequence, s.MonotonicMs, $"flasks {before}->{now}"));
        else events.Add(new EngineEvent(EngineEventKind.FlasksRefilled, battle, s.Sequence, s.MonotonicMs, $"flasks {before}->{now}"));
        if (before > 0 && now == 0)
            events.Add(new EngineEvent(EngineEventKind.FlasksEmpty, battle, s.Sequence, s.MonotonicMs, "flask total reached 0"));
    }

    // Enemy count and battle duration, both only inside a battle.
    private void UpdateBattleExtras(Snapshot s, bool battleStartedNow, List<EngineEvent> events)
    {
        var count = s.Combat.Valid ? s.Combat.InCombat : null;
        var prevCount = _prevEnemies;
        _prevEnemies = count;
        if (!_inBattle)
        {
            _prevBattleMs = null;
            return;
        }
        if (count is { } c)
        {
            // The sample that starts a battle compares against 0, so "at least 1" style rules fire with it.
            var before = battleStartedNow ? 0 : prevCount;
            foreach (var n in Thresholds(EngineEventKind.EnemiesAtLeast).OrderBy(n => n))
                if (before is { } b && b < n && c >= n)
                    events.Add(new EngineEvent(EngineEventKind.EnemiesAtLeast, _battleId, s.Sequence, s.MonotonicMs, $"{c} enemies engaged", n));
        }
        var elapsed = s.MonotonicMs - _battleStartMs;
        var prevElapsed = _prevBattleMs;
        _prevBattleMs = elapsed;
        if (prevElapsed is not { } pe) return;
        foreach (var sec in Thresholds(EngineEventKind.BattleLong).OrderBy(x => x))
            if (pe < sec * 1000 && elapsed >= sec * 1000)
                events.Add(new EngineEvent(EngineEventKind.BattleLong, _battleId, s.Sequence, s.MonotonicMs, $"battle lasted {sec:0} s", sec));
    }

    // Shachi, ultimate and spirit edges (suite 0.2.0+ telemetry; older lines carry no values and never fire).
    private void UpdateValues(Snapshot s, List<EngineEvent> events)
    {
        var v = s.Values ?? ValuesSample.Unknown;
        var prev = _prevValues;
        _prevValues = (Cur(v.Shachi), Cur(v.Ultimate), Cur(v.Spirit));
        var battle = _inBattle ? _battleId : 0;
        void Add(EngineEventKind kind, string reason) => events.Add(new EngineEvent(kind, battle, s.Sequence, s.MonotonicMs, reason));
        const double eps = 0.001;

        if (prev.shachi is { } ps && Cur(v.Shachi) is { } cs && v.Shachi.Max is { } ms && ms > 0 && ps < ms - eps && cs >= ms - eps)
            Add(EngineEventKind.ShachiFull, "shachi full");
        if (prev.ult is { } pu && Cur(v.Ultimate) is { } cu && v.Ultimate.Max is { } mu && mu > 0)
        {
            if (pu < mu - eps && cu >= mu - eps) Add(EngineEventKind.UltimateReady, "ultimate full");
            if (pu >= mu - eps && cu < mu / 2) Add(EngineEventKind.UltimateUsed, "ultimate spent");
        }
        if (prev.spirit is { } pp && Cur(v.Spirit) is { } cp && v.Spirit.Max is { } mp && mp > 0)
        {
            if (pp < mp - eps && cp >= mp - eps) Add(EngineEventKind.SpiritFull, "spirit full");
            if (pp > eps && cp <= eps) Add(EngineEventKind.SpiritEmpty, "spirit empty");
        }
    }

    private static double? Cur(HealthSample s) => s.Valid ? s.Current : null;

    private void EndBattle(Snapshot s, string reason, List<EngineEvent> events)
    {
        _inBattle = false;
        _zeroSinceMs = null;
        _enterRun = 0;
        events.Add(new EngineEvent(EngineEventKind.BattleEnded, _battleId, s.Sequence, s.MonotonicMs, reason));
    }

    private void ResetTracking()
    {
        _enterRun = 0;
        _zeroSinceMs = null;
        _prevRatio = null;
        _prevFlaskTotal = null;
        _prevEnemies = null;
        _prevBattleMs = null;
        _prevValues = default;
    }
}
