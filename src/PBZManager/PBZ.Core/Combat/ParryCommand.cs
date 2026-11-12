namespace PBZ.Core.Combat;

// One command from PBZSuite (runtime/parry-cmd.txt). Current lines:
//   "PBZPARRY <session> <seq> <deflect|hold|dodge> <pressInMs> <hitInMs> end"   (hold = keep the deflect key down)
// where pressInMs is when to press the key and hitInMs when the attack lands, both in real milliseconds from the
// moment the line was written. 1.0.1 lines without the two numbers mean "press now". The trailing "end" marks a
// complete line; anything else is ignored and read again later. The file holds the last few commands.
public sealed record ParryCommand(string Session, long Seq, bool Dodge, int PressInMs = 0, int HitInMs = 0, bool Hold = false)
{
    public static ParryCommand? TryParse(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        var p = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (p.Length is not (5 or 7) || p[0] != "PBZPARRY" || p[^1] != "end" || !long.TryParse(p[2], out var seq)) return null;
        bool dodge = false, hold = false;
        if (p[3] == "dodge") dodge = true;
        else if (p[3] == "hold") hold = true;
        else if (p[3] != "deflect") return null;
        if (p.Length == 5) return new ParryCommand(p[1], seq, dodge, Hold: hold);
        if (!int.TryParse(p[4], out var pressIn) || !int.TryParse(p[5], out var hitIn)) return null;
        return new ParryCommand(p[1], seq, dodge, Math.Clamp(pressIn, 0, 5000), Math.Clamp(hitIn, 0, 5000), hold);
    }

    // Every complete command in the file, in order.
    public static List<ParryCommand> ParseAll(string? text) =>
        string.IsNullOrEmpty(text) ? [] : text.Split('\n').Select(TryParse).OfType<ParryCommand>().ToList();

    // True if `next` is a command not seen yet: a different game session, or a higher number in the same one.
    public static bool IsNew(ParryCommand? last, ParryCommand next) =>
        last is null || last.Session != next.Session || next.Seq > last.Seq;
}

// Keys the manager can press for deflect / dodge. Keyboard keys use scan codes (what games read); mouse buttons
// use mouse events.
public sealed record PressKey(string Id, string Name, ushort ScanCode, bool Extended, int MouseButton)
{
    public bool IsMouse => MouseButton > 0;

    // What a combo box shows for the selected key.
    public override string ToString() => Name;

    public static readonly IReadOnlyList<PressKey> All =
    [
        new("RightMouse", "鼠标右键", 0, false, 2),
        new("LeftMouse", "鼠标左键", 0, false, 1),
        new("MiddleMouse", "鼠标中键", 0, false, 3),
        new("LeftShift", "左 Shift", 0x2A, false, 0),
        new("SpaceBar", "空格", 0x39, false, 0),
        new("LeftControl", "左 Ctrl", 0x1D, false, 0),
        new("LeftAlt", "左 Alt", 0x38, false, 0),
        new("Q", "Q", 0x10, false, 0),
        new("E", "E", 0x12, false, 0),
        new("R", "R", 0x13, false, 0),
        new("F", "F", 0x21, false, 0),
        new("C", "C", 0x2E, false, 0),
        new("V", "V", 0x2F, false, 0),
    ];

    public static PressKey Find(string? id, string fallback) =>
        All.FirstOrDefault(k => k.Id == id) ?? All.First(k => k.Id == fallback);
}

public readonly record struct KeyEvent(bool Dodge, bool Down);

// Turns scheduled presses into key down / up events at the right moments. Pure (time is passed in), so it is tested
// without real input. Deflect taps by default; in combo-hold mode the deflect key stays down while further hits of
// a combo are due and is released a little after the last one. Dodge always taps (a long Shift press is sprint).
public sealed class ParryScheduler
{
    public const int TapMs = 40;
    public const int RepressGapMs = 15;   // key up before pressing again, so the game sees a new press
    public const int HoldTailMs = 120;    // combo hold: keep the key down this long after the last hit

    private readonly List<(long PressAt, long HitAt, bool Dodge, bool Hold, long Seq)> _queue = [];
    private readonly KeyState[] _keys = [new(), new()];

    private sealed class KeyState
    {
        public bool Down;
        public long ReleaseAt;
        public long? PendingDownAt;
        public long PendingReleaseAt;
    }

    public bool ComboHold { get; set; }
    public int Presses { get; private set; }
    public int SkippedNotForeground { get; private set; }

    public void Add(ParryCommand c, long nowMs) =>
        _queue.Add((nowMs + c.PressInMs, nowMs + Math.Max(c.HitInMs, c.PressInMs), c.Dodge, c.Hold, c.Seq));

    // Commands that reached their press time in the last Tick: (seq, pressed, ms late). For the relay's log.
    public List<(long Seq, bool Pressed, long LateMs)> LastHandled { get; } = [];

    public void Clear() => _queue.Clear();

    // Advances to nowMs and returns the key events to send, in order.
    public List<KeyEvent> Tick(long nowMs, bool gameForeground)
    {
        var events = new List<KeyEvent>();
        for (var k = 0; k < 2; k++)
        {
            var s = _keys[k];
            if (s.Down && nowMs >= s.ReleaseAt) { s.Down = false; events.Add(new KeyEvent(k == 1, false)); }
            if (s.PendingDownAt is { } at && nowMs >= at)
            {
                s.PendingDownAt = null;
                s.Down = true;
                s.ReleaseAt = s.PendingReleaseAt;
                events.Add(new KeyEvent(k == 1, true));
            }
        }

        var due = _queue.Where(q => q.PressAt <= nowMs).OrderBy(q => q.PressAt).ToList();
        _queue.RemoveAll(q => q.PressAt <= nowMs);
        LastHandled.Clear();
        foreach (var q in due)
        {
            LastHandled.Add((q.Seq, gameForeground, nowMs - q.PressAt));
            if (!gameForeground) { SkippedNotForeground++; continue; }
            Presses++;
            var s = _keys[q.Dodge ? 1 : 0];
            if ((ComboHold || q.Hold) && !q.Dodge)
            {
                if (!s.Down && s.PendingDownAt is null) { s.Down = true; events.Add(new KeyEvent(false, true)); s.ReleaseAt = 0; }
                var until = Math.Max(q.HitAt + HoldTailMs, nowMs + TapMs);
                if (s.PendingDownAt is not null) s.PendingReleaseAt = Math.Max(s.PendingReleaseAt, until);
                else s.ReleaseAt = Math.Max(s.ReleaseAt, until);
                continue;
            }
            if (s.Down)
            {
                // Still down from the previous press: let go and press again shortly, as a new press. If that press
                // was a hold, keep holding until it was due to end, so the held attack is still covered.
                s.Down = false;
                events.Add(new KeyEvent(q.Dodge, false));
                s.PendingDownAt = nowMs + RepressGapMs;
                s.PendingReleaseAt = Math.Max(nowMs + RepressGapMs + TapMs, s.ReleaseAt);
            }
            else if (s.PendingDownAt is null)
            {
                s.Down = true;
                s.ReleaseAt = nowMs + TapMs;
                events.Add(new KeyEvent(q.Dodge, true));
            }
            // A press already pending covers this one as well.
        }

        // Never leave a key held while another window is in front.
        if (!gameForeground) events.AddRange(ReleaseAll());
        return events;
    }

    // Lets go of every key (switching off, closing, game not in front).
    public List<KeyEvent> ReleaseAll()
    {
        var events = new List<KeyEvent>();
        for (var k = 0; k < 2; k++)
        {
            var s = _keys[k];
            s.PendingDownAt = null;
            if (s.Down) { s.Down = false; events.Add(new KeyEvent(k == 1, false)); }
        }
        return events;
    }
}
