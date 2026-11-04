namespace PBZ.Core.Combat;

// One command from PBZSuite (runtime/parry-cmd.txt): "PBZPARRY <session> <seq> <deflect|dodge> end".
// The trailing "end" marks a complete write; anything else is ignored and read again later.
public sealed record ParryCommand(string Session, long Seq, bool Dodge)
{
    public static ParryCommand? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var p = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (p.Length != 5 || p[0] != "PBZPARRY" || p[4] != "end" || !long.TryParse(p[2], out var seq)) return null;
        return p[3] switch
        {
            "deflect" => new ParryCommand(p[1], seq, false),
            "dodge" => new ParryCommand(p[1], seq, true),
            _ => null,
        };
    }

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
