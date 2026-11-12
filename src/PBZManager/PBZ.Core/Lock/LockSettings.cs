using System.Text.Json;
using System.Text.Json.Serialization;

namespace PBZ.Core.Lock;

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Ctrl = 2,
    Shift = 4,
}

// A global hotkey such as "Ctrl+F1". Key is a WPF System.Windows.Input.Key name (F1, D1, NumPad3, A …);
// the manager turns it into a virtual-key code. Kept as text so settings stay readable and Core stays UI-free.
public sealed record Hotkey(HotkeyModifiers Modifiers, string Key)
{
    public static Hotkey? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var mods = HotkeyModifiers.None;
        string? key = null;
        foreach (var raw in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl" or "control": mods |= HotkeyModifiers.Ctrl; break;
                case "alt": mods |= HotkeyModifiers.Alt; break;
                case "shift": mods |= HotkeyModifiers.Shift; break;
                default:
                    if (key is not null) return null; // two non-modifier keys
                    key = raw;
                    break;
            }
        }
        return key is null ? null : new Hotkey(mods, key);
    }

    // Canonical form, also used as the stored text: modifiers in fixed order, then the key name.
    public override string ToString() => Join(Key);

    // What the user sees: digit keys as "1" instead of "D1", numpad keys as "小键盘1".
    public string Display => Join(Key switch
    {
        ['D', var d] when char.IsDigit(d) => d.ToString(),
        _ when Key.StartsWith("NumPad", StringComparison.Ordinal) => "小键盘" + Key[6..],
        _ => Key,
    });

    private string Join(string key)
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(HotkeyModifiers.Ctrl)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        parts.Add(key);
        return string.Join("+", parts);
    }
}

public static class LockActions
{
    public const string Toggle = "toggle";
    public const string Health = "health";
    public const string Shachi = "shachi";
    public const string Ultimate = "ultimate";
    public const string Spirit = "spirit";
    public const string Parry = "parry"; // auto deflect / dodge master switch (T5b), shares the hotkey machinery
    public const string MusicStop = "musicstop";     // fade out the song that is playing
    public const string MusicToggle = "musictoggle"; // condition player on / off
    public const string SavePos = "savepos";   // player tools: remember where the character stands
    public const string Teleport = "teleport"; // go back to the last remembered place
    public const string Fly = "fly";           // fly with collision off
    public const string FlyUp = "flyup";       // move up / down a little (works with or without flying)
    public const string FlyDown = "flydown";

    public static readonly string[] All = [Toggle, Health, Shachi, Ultimate, Spirit, Parry, MusicStop, MusicToggle,
        SavePos, Teleport, Fly, FlyUp, FlyDown];
    public static readonly string[] Items = [Health, Shachi, Ultimate, Spirit];

    public static string Name(string action) => action switch
    {
        Toggle => "锁定总开关",
        Health => "血量",
        Shachi => "杀气",
        Ultimate => "绝技能量",
        Spirit => "影/魂点数",
        Parry => "自动弹反/闪避",
        MusicStop => "停止当前歌曲",
        MusicToggle => "条件播放器开关",
        SavePos => "记录当前位置",
        Teleport => "传送到记录点",
        Fly => "飞行开关",
        FlyUp => "上升",
        FlyDown => "下降",
        _ => action,
    };
}

// Manager-only lock preferences (%LOCALAPPDATA%\PBZManager\lock.json). The master switch itself is never saved:
// every manager start begins with the lock off (product rule: write features default to off).
public sealed record LockSettings(bool Health, bool Shachi, bool Ultimate, bool Spirit, bool Sound, Dictionary<string, string>? Hotkeys)
{
    public static Dictionary<string, string> DefaultHotkeys() => new()
    {
        [LockActions.Toggle] = "Ctrl+F1",
        [LockActions.Health] = "Ctrl+F2",
        [LockActions.Shachi] = "Ctrl+F3",
        [LockActions.Ultimate] = "Ctrl+F4",
        [LockActions.Spirit] = "Ctrl+F5",
        [LockActions.Parry] = "F7",
        [LockActions.MusicStop] = "Ctrl+F6",
        [LockActions.MusicToggle] = "Ctrl+F8",
        [LockActions.SavePos] = "Ctrl+F9",
        [LockActions.Teleport] = "Ctrl+F10",
        [LockActions.Fly] = "Ctrl+F11",
        [LockActions.FlyUp] = "Ctrl+Up",
        [LockActions.FlyDown] = "Ctrl+Down",
    };

    public static LockSettings Default => new(true, true, true, true, true, DefaultHotkeys());

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    // Auto deflect / dodge options (T5b); the master switch is never saved, like the lock's.
    public bool ParryDeflect { get; init; } = true;
    public bool ParryDodge { get; init; } = true;
    public int ParryLeadMs { get; init; } = 120;
    // Real keys the manager presses (PressKey ids); defaults are the game's default bindings found 2026-11-04.
    public string ParryDeflectKey { get; init; } = "RightMouse";
    public string ParryDodgeKey { get; init; } = "LeftShift";
    // Keep the deflect key down through a combo instead of one tap per hit (experimental, off by default).
    public bool ParryComboHold { get; init; }
    // Per attack type answer ("0".."5" -> deflect/hold/dodge/none); missing types use AttackTypes defaults.
    public Dictionary<string, string>? ParryActions { get; init; }

    // Fixed values per item; an item missing here is locked to full.
    public Dictionary<string, double>? Values { get; init; }

    // Null = lock to full.
    public double? Value(string action) => Values is not null && Values.TryGetValue(action, out var v) ? v : null;

    public LockSettings WithValue(string action, double? value)
    {
        var map = new Dictionary<string, double>(Values ?? []);
        if (value is { } v) map[action] = v;
        else map.Remove(action);
        return this with { Values = map };
    }

    // Checks a typed value. Empty text means "full". Health must stay above 0 (0 would kill the character).
    public static bool TryParseValue(string action, string? text, out double? value, out string error)
    {
        value = null;
        error = "";
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (!double.TryParse(text.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)
            || double.IsNaN(v) || double.IsInfinity(v))
        {
            error = "请输入数字，或留空表示锁满";
            return false;
        }
        if (action == LockActions.Health ? v < 1 : v < 0)
        {
            error = action == LockActions.Health ? "血量至少为 1" : "数值不能小于 0";
            return false;
        }
        value = action == LockActions.Spirit ? Math.Round(v, MidpointRounding.AwayFromZero) : Math.Round(v, 1, MidpointRounding.AwayFromZero);
        return true;
    }

    public string HotkeyText(string action) => Hotkeys is not null && Hotkeys.TryGetValue(action, out var t) ? t : "";

    public LockSettings WithHotkey(string action, string text)
    {
        var map = new Dictionary<string, string>(Hotkeys ?? []) { [action] = text };
        return this with { Hotkeys = map };
    }

    // Actions sharing one hotkey with an earlier action, so the window can flag them.
    public IReadOnlyList<string> Duplicates()
    {
        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var dup = new List<string>();
        foreach (var a in LockActions.All)
        {
            var hk = Hotkey.Parse(HotkeyText(a))?.ToString();
            if (hk is null) continue;
            if (!seen.TryAdd(hk, a)) dup.Add(a);
        }
        return dup;
    }

    public static LockSettings Load(string file)
    {
        try
        {
            if (!File.Exists(file)) return Default;
            var s = JsonSerializer.Deserialize<LockSettings>(File.ReadAllText(file), Json) ?? Default;
            var keys = DefaultHotkeys();
            foreach (var (k, v) in s.Hotkeys ?? []) if (keys.ContainsKey(k)) keys[k] = Hotkey.Parse(v)?.ToString() ?? "";
            var values = new Dictionary<string, double>();
            foreach (var (k, v) in s.Values ?? [])
                if (LockActions.Items.Contains(k) && TryParseValue(k, v.ToString(System.Globalization.CultureInfo.InvariantCulture), out var ok, out _) && ok is { } good)
                    values[k] = good;
            return s with { Hotkeys = keys, Values = values, ParryLeadMs = Math.Clamp(s.ParryLeadMs, 0, 300) };
        }
        catch (JsonException)
        {
            return Default;
        }
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
