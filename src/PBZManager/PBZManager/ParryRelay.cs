using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using PBZ.Core.Combat;

namespace PBZManager;

// Watches runtime/parry-cmd.txt (written by PBZSuite) on a background thread and presses the real deflect / dodge key
// for each new command. Presses only while the game window is in the foreground, so nothing leaks into other programs.
// Injected Enhanced Input does nothing in this game, so real key events are the working path (evidence/t5b).
public sealed class ParryRelay : IDisposable
{
    private const int TickMs = 1;

    private readonly string _file;
    private readonly Thread _thread;
    private readonly ParryScheduler _scheduler = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private volatile bool _running = true;
    private volatile bool _enabled;
    private ParryCommand? _last;
    private bool _baselined;

    private readonly StreamWriter? _log;

    // logFile: one line per command received and per key press, to trace a missed deflect afterwards.
    public ParryRelay(string runtimeDir, string? logFile = null)
    {
        _file = Path.Combine(runtimeDir, "parry-cmd.txt");
        try { if (logFile is not null) _log = new StreamWriter(logFile, append: true) { AutoFlush = true }; }
        catch (IOException) { _log = null; }
        _thread = new Thread(Run) { IsBackground = true, Name = "ParryRelay", Priority = ThreadPriority.Highest };
        _thread.Start();
    }

    public bool Enabled { get => _enabled; set => _enabled = value; }
    public volatile PressKey DeflectKey = PressKey.Find("RightMouse", "RightMouse");
    public volatile PressKey DodgeKey = PressKey.Find("LeftShift", "LeftShift");

    // Combo-hold mode: keep the deflect key down through a combo instead of tapping once per hit.
    public bool ComboHold { get => _scheduler.ComboHold; set => _scheduler.ComboHold = value; }

    public int Presses => _scheduler.Presses;
    public int SkippedNotForeground => _scheduler.SkippedNotForeground;

    private void Run()
    {
        TimeBeginPeriod(1);
        try
        {
            while (_running)
            {
                Thread.Sleep(TickMs);
                var now = _clock.ElapsedMilliseconds;
                if (!_enabled)
                {
                    // Switched off: let go of anything held, forget the queue, and treat what is in the file as old.
                    if (_baselined) { Apply(_scheduler.ReleaseAll()); _scheduler.Clear(); }
                    _baselined = false;
                    continue;
                }
                var commands = ParryCommand.ParseAll(ReadFile());
                if (!_baselined)
                {
                    _last = commands.LastOrDefault();
                    _baselined = true;
                    continue;
                }
                foreach (var c in commands)
                {
                    if (!ParryCommand.IsNew(_last, c)) continue;
                    _last = c;
                    _scheduler.Add(c, now);
                    Log($"#{c.Seq} received {(c.Dodge ? "dodge" : c.Hold ? "hold" : "deflect")} press in {c.PressInMs} ms, hit in {c.HitInMs} ms");
                }
                Apply(_scheduler.Tick(now, IsGameForeground()));
                foreach (var (seq, pressed, late) in _scheduler.LastHandled)
                    Log(pressed ? $"#{seq} pressed ({late} ms late)" : $"#{seq} skipped: game window not in front");
            }
        }
        finally
        {
            Apply(_scheduler.ReleaseAll());
            TimeEndPeriod(1);
        }
    }

    private void Log(string line)
    {
        try { _log?.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {line}"); }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException) { } // closing while the thread logs
    }

    private void Apply(List<KeyEvent> events)
    {
        foreach (var e in events) Send(Make(e.Dodge ? DodgeKey : DeflectKey, e.Down));
    }

    private string? ReadFile()
    {
        try
        {
            using var fs = new FileStream(_file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var r = new StreamReader(fs);
            return r.ReadToEnd();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    private int _gamePid;

    private bool IsGameForeground()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return false;
        GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0) return false;
        if (pid == _gamePid) return true;
        try
        {
            using var p = Process.GetProcessById((int)pid);
            if (!p.ProcessName.Equals("PBZ", StringComparison.OrdinalIgnoreCase)) return false;
            _gamePid = (int)pid;
            return true;
        }
        catch (ArgumentException) { return false; }
    }

    private static INPUT Make(PressKey key, bool down)
    {
        if (key.IsMouse)
        {
            uint flag = (key.MouseButton, down) switch
            {
                (1, true) => 0x0002, (1, false) => 0x0004,  // left
                (2, true) => 0x0008, (2, false) => 0x0010,  // right
                (3, true) => 0x0020, _ => 0x0040,           // middle
            };
            return new INPUT { type = 0, u = new InputUnion { mi = new MOUSEINPUT { dwFlags = flag } } };
        }
        uint kflags = 0x0008; // KEYEVENTF_SCANCODE
        if (!down) kflags |= 0x0002; // KEYEVENTF_KEYUP
        if (key.Extended) kflags |= 0x0001;
        return new INPUT { type = 1, u = new InputUnion { ki = new KEYBDINPUT { wScan = key.ScanCode, dwFlags = kflags } } };
    }

    private static void Send(INPUT input) => SendInput(1, [input], Marshal.SizeOf<INPUT>());

    public void Dispose()
    {
        _running = false;
        _thread.Join(500);
        _log?.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT { public uint type; public InputUnion u; }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static extern uint TimeBeginPeriod(uint ms);

    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    private static extern uint TimeEndPeriod(uint ms);
}
