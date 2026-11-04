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
    private const int PollMs = 2;
    private const int HoldMs = 40; // short tap: a longer Shift press turns into sprint

    private readonly string _file;
    private readonly Thread _thread;
    private volatile bool _running = true;
    private ParryCommand? _last;
    private bool _baselined;

    public ParryRelay(string runtimeDir)
    {
        _file = Path.Combine(runtimeDir, "parry-cmd.txt");
        _thread = new Thread(Run) { IsBackground = true, Name = "ParryRelay", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    public volatile bool Enabled;
    public volatile PressKey DeflectKey = PressKey.Find("RightMouse", "RightMouse");
    public volatile PressKey DodgeKey = PressKey.Find("LeftShift", "LeftShift");

    public int Presses => _presses;
    public int SkippedNotForeground => _skipped;
    private int _presses;
    private int _skipped;

    // Raised on the relay thread.
    public event Action<ParryCommand, bool>? Handled;

    private void Run()
    {
        TimeBeginPeriod(1);
        try
        {
            while (_running)
            {
                Thread.Sleep(PollMs);
                var cmd = ParryCommand.TryParse(ReadFile());
                // Commands already in the file when the relay is switched on are old: remember them, never press them.
                if (!Enabled)
                {
                    _baselined = false;
                    continue;
                }
                if (!_baselined)
                {
                    _last = cmd;
                    _baselined = true;
                    continue;
                }
                if (cmd is null || !ParryCommand.IsNew(_last, cmd)) continue;
                _last = cmd;
                var pressed = false;
                if (IsGameForeground())
                {
                    Press(cmd.Dodge ? DodgeKey : DeflectKey);
                    Interlocked.Increment(ref _presses);
                    pressed = true;
                }
                else Interlocked.Increment(ref _skipped);
                Handled?.Invoke(cmd, pressed);
            }
        }
        finally { TimeEndPeriod(1); }
    }

    private string? ReadFile()
    {
        try
        {
            using var fs = new FileStream(_file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var r = new StreamReader(fs);
            return r.ReadLine();
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

    private static void Press(PressKey key)
    {
        Send(Make(key, down: true));
        Thread.Sleep(HoldMs);
        Send(Make(key, down: false));
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
