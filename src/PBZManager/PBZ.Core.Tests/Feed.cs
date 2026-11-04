using PBZ.Core.Events;
using PBZ.Core.Telemetry;

namespace PBZ.Core.Tests;

// Builds synthetic snapshots; one sample every 200 ms unless a time is given.
internal sealed class Feed
{
    private long _seq;
    private long _ms;

    public string Session { get; set; } = "00000001aaaa";
    public int Generation { get; set; } = 1;
    public double Max { get; set; } = 1000;
    public int Top { get; set; } = 3;
    public int Bottom { get; set; } = 4;
    public bool FlaskValid { get; set; } = true;
    public int Combat { get; set; }
    public bool CombatValid { get; set; } = true;
    public int Dead { get; set; }
    public string Lifecycle { get; set; } = "playing";
    public double Hp { get; set; } = 1000;
    public ValuesSample? Values { get; set; }
    public BossSample? Boss { get; set; }

    public Snapshot Next(long stepMs = 200)
    {
        _seq++;
        _ms += stepMs;
        return new Snapshot(
            1, Session, _seq, _ms, null, Lifecycle, Generation,
            new HealthSample(Hp, Max, true),
            new CombatSample(CombatValid ? Combat : null, CombatValid ? Dead : null, CombatValid),
            new FlaskSample(
                new FlaskSlot("FeiJiu", FlaskValid ? Top : null, FlaskValid),
                new FlaskSlot("Kuye", FlaskValid ? Bottom : null, FlaskValid),
                FlaskValid ? Top + Bottom : null,
                FlaskValid),
            0, new ModulesState(true, false, false), [], Values, null, Boss);
    }

    public List<EngineEvent> Run(EventEngine engine, int samples, long stepMs = 200)
    {
        var all = new List<EngineEvent>();
        for (var i = 0; i < samples; i++) all.AddRange(engine.Process(Next(stepMs)));
        return all;
    }
}

internal static class EventListExtensions
{
    public static int Count(this IEnumerable<EngineEvent> events, EngineEventKind kind) => events.Count(e => e.Kind == kind);
}
