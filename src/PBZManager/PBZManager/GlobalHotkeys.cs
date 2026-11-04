using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using PBZ.Core.Lock;

namespace PBZManager;

// System-wide hotkeys (Win32 RegisterHotKey), so they work while the game has focus, also in full screen.
public sealed class GlobalHotkeys : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const uint ModNoRepeat = 0x4000;

    private readonly HwndSource _source;
    private readonly Dictionary<int, string> _actions = [];
    private int _nextId = 0xB000;

    public event Action<string>? Pressed;

    public GlobalHotkeys(Window window)
    {
        _source = HwndSource.FromHwnd(new WindowInteropHelper(window).EnsureHandle());
        _source.AddHook(Hook);
    }

    // Replaces all registrations. Returns the actions whose hotkey could not be registered (taken by another program
    // or not a valid key).
    public List<string> Apply(IEnumerable<(string Action, Hotkey? Key)> bindings)
    {
        Clear();
        var failed = new List<string>();
        foreach (var (action, key) in bindings)
        {
            if (key is null) continue;
            if (!TryVirtualKey(key.Key, out var vk)) { failed.Add(action); continue; }
            var id = _nextId++;
            if (RegisterHotKey(_source.Handle, id, (uint)key.Modifiers | ModNoRepeat, vk)) _actions[id] = action;
            else failed.Add(action);
        }
        return failed;
    }

    public void Clear()
    {
        foreach (var id in _actions.Keys) UnregisterHotKey(_source.Handle, id);
        _actions.Clear();
    }

    public static bool TryVirtualKey(string keyName, out uint vk)
    {
        vk = 0;
        if (!Enum.TryParse<Key>(keyName, out var key) || key == Key.None) return false;
        vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        return vk != 0;
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && _actions.TryGetValue(wParam.ToInt32(), out var action))
        {
            handled = true;
            Pressed?.Invoke(action);
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Clear();
        _source.RemoveHook(Hook);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
