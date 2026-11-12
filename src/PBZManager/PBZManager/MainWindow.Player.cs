using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PBZ.Core.Lock;
using PBZ.Core.Player;
using PBZ.Core.Suite;
using PBZ.Core.Telemetry;

namespace PBZManager;

// A saved place in the list; Name can be edited in the grid.
public sealed class WaypointRow(Waypoint p) : System.ComponentModel.INotifyPropertyChanged
{
    private string _name = p.Name;
    public Waypoint Point { get; private set; } = p;
    public string Name
    {
        get => _name;
        set { _name = value; Point = Point with { Name = value }; PropertyChanged?.Invoke(this, new(nameof(Name))); }
    }
    public string Where => Point.Where;
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}

// "玩家工具" page: game / move speed, damage multipliers, unlimited medicine, fly, teleport, XP and level.
// Like the value lock, nothing here is remembered between runs: every start is neutral, and closing resets the game.
public partial class MainWindow
{
    // A property, not a static field: static fields in another part of this partial class may not be set yet.
    private static string WaypointFile => Path.Combine(DataDir, "teleports.json");
    private SuiteCheats _cheats = SuiteCheats.Neutral;
    private readonly ObservableCollection<WaypointRow> _waypoints = [];
    private readonly HashSet<long> _pendingPlayerOps = [];
    private PlayerOpResult? _lastTeleport; // for "undo": where the last successful teleport started
    private long _lastPlayerResultSeq;

    private void InitPlayer()
    {
        foreach (var p in WaypointStore.Load(WaypointFile)) _waypoints.Add(new WaypointRow(p));
        WaypointGrid.ItemsSource = _waypoints;
        ShowCheats();
    }

    private void ShowCheats()
    {
        GameSpeedBox.Text = Fmt(_cheats.GameSpeed);
        MoveSpeedBox.Text = Fmt(_cheats.MoveSpeed);
        DamageBox.Text = Fmt(_cheats.Damage);
        TakenBox.Text = Fmt(_cheats.Taken);
        _loadingSettings = true;
        MedicineBox.IsChecked = _cheats.Medicine;
        FlyBox.IsChecked = _cheats.Fly;
        _loadingSettings = false;
        static string Fmt(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
    }

    // Writes the cheats to the game when they differ from what the config holds.
    private void PushCheats()
    {
        if (_game is null || _installer?.GetState() is not (PBZ.Core.Suite.SuiteState.Installed or PBZ.Core.Suite.SuiteState.Outdated)) return;
        try
        {
            var current = SuiteConfigStore.Read(_game.RuntimeDir);
            var wanted = _cheats.Clamped();
            if ((current.Cheats ?? SuiteCheats.Neutral) == wanted) return;
            var written = SuiteConfigStore.Write(_game.RuntimeDir, current with { Cheats = wanted });
            Journal($"cheats revision={written.Revision} {wanted}");
        }
        catch (IOException ex) { Say("写入设置失败：" + ex.Message); }
    }

    private void SetCheats(SuiteCheats next, string message)
    {
        if (next.AnyActive && LockBlockedReason() is { } why) { Say("无法启用：" + why); ShowCheats(); return; }
        _cheats = next.Clamped();
        ShowCheats();
        PushCheats();
        Say(message);
    }

    private void CheatBox_Commit(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox { Tag: string what } box) return;
        if (!double.TryParse(box.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
        {
            Say("请填数字，例如 1.5");
            ShowCheats();
            return;
        }
        var next = what switch
        {
            "game" => _cheats with { GameSpeed = v },
            "move" => _cheats with { MoveSpeed = v },
            "damage" => _cheats with { Damage = v },
            _ => _cheats with { Taken = v },
        };
        if (next.Clamped() == _cheats) { ShowCheats(); return; }
        var c = next.Clamped();
        SetCheats(next, what switch
        {
            "game" => $"游戏速度改为 {c.GameSpeed:0.##} 倍",
            "move" => $"移动速度改为 {c.MoveSpeed:0.##} 倍",
            "damage" => $"造成伤害改为 {c.Damage:0.##} 倍",
            _ => $"受到伤害改为 {c.Taken:0.##} 倍",
        });
    }

    private void CheatBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) CheatBox_Commit(sender, e);
    }

    private void MedicineBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings) return;
        var on = MedicineBox.IsChecked == true;
        SetCheats(_cheats with { Medicine = on }, on ? "无限药：开" : "无限药：关");
    }

    private void FlyBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings) return;
        SetFly(FlyBox.IsChecked == true);
    }

    private void SetFly(bool on)
    {
        SetCheats(_cheats with { Fly = on }, on ? "飞行：开（可穿墙，用上升 / 下降调整高度）" : "飞行：关");
        Cue(on);
    }

    private void ResetCheats_Click(object sender, RoutedEventArgs e) => SetCheats(SuiteCheats.Neutral, "玩家工具已全部恢复正常");

    // ----- one-shot ops -----
    private void SendPlayerOp(PlayerOp op, string message)
    {
        if (!ItemsReady(out var why)) { Say("无法操作：" + why); return; }
        try
        {
            var current = SuiteConfigStore.Read(_game!.RuntimeDir);
            var next = PlayerEdits.Append(current, op);
            _pendingPlayerOps.Add(next.PlayerOps![^1].Seq);
            SuiteConfigStore.Write(_game.RuntimeDir, next);
            Say(message);
        }
        catch (IOException ex) { Say("写入操作失败：" + ex.Message); }
    }

    private void Nudge(double forwardCm, double upCm, string what) =>
        SendPlayerOp(new PlayerOp(0, "nudge", X: forwardCm, Z: upCm), what);

    private void Up_Click(object sender, RoutedEventArgs e) => Nudge(0, 200, "上升 2 米");
    private void Down_Click(object sender, RoutedEventArgs e) => Nudge(0, -200, "下降 2 米");
    private void Forward_Click(object sender, RoutedEventArgs e) => Nudge(500, 0, "向前 5 米");

    private void SavePosition()
    {
        if (_last?.Player is not { Valid: true, X: { } x, Y: { } y, Z: { } z } p) { Say("读不到角色位置：游戏没在运行，或游戏内模块不是 1.0.3"); return; }
        var name = string.IsNullOrWhiteSpace(WaypointName.Text) ? $"记录点 {DateTime.Now:MM-dd HH:mm:ss}" : WaypointName.Text.Trim();
        var row = new WaypointRow(new Waypoint(name, p.World ?? "?", x, y, z));
        _waypoints.Insert(0, row);
        WaypointGrid.SelectedItem = row;
        WaypointName.Text = "";
        SaveWaypoints();
        Say($"已记录位置：{name}");
        Cue(true);
    }

    private void SavePosition_Click(object sender, RoutedEventArgs e) => SavePosition();

    // Teleports to the selected place, or the newest one when nothing is selected (hotkey).
    private void TeleportToSaved()
    {
        var row = WaypointGrid.SelectedItem as WaypointRow ?? _waypoints.FirstOrDefault();
        if (row is null) { Say("还没有记录点"); return; }
        if (_last?.Player?.World is { } world && world != row.Point.World)
        {
            Say($"“{row.Name}”在另一张地图（{row.Point.World}），当前是 {world}，不能直接传送");
            return;
        }
        SendPlayerOp(new PlayerOp(0, "teleport", row.Point.X, row.Point.Y, row.Point.Z), $"传送到：{row.Name}");
    }

    private void Teleport_Click(object sender, RoutedEventArgs e) => TeleportToSaved();

    private void UndoTeleport_Click(object sender, RoutedEventArgs e)
    {
        if (_lastTeleport is not { FromX: { } x, FromY: { } y, FromZ: { } z }) { Say("没有可以撤销的传送"); return; }
        SendPlayerOp(new PlayerOp(0, "teleport", x, y, z), "回到上次传送前的位置");
    }

    private void DeleteWaypoint_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in WaypointGrid.SelectedItems.OfType<WaypointRow>().ToList()) _waypoints.Remove(row);
        SaveWaypoints();
    }

    private void WaypointGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e) =>
        Dispatcher.BeginInvoke(SaveWaypoints); // after the edit is committed to the row

    private void SaveWaypoints()
    {
        try { WaypointStore.Save(WaypointFile, _waypoints.Select(r => r.Point)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Say("保存记录点失败：" + ex.Message); }
    }

    private void ApplyLevel_Click(object sender, RoutedEventArgs e)
    {
        var sent = false;
        if (LevelNew.Text.Trim() is { Length: > 0 } lt)
        {
            if (!int.TryParse(lt, out var level) || level < 1 || level > 999) { Say("等级填 1–999 的整数"); return; }
            SendPlayerOp(new PlayerOp(0, "level", Value: level), $"等级改为 {level}");
            sent = true;
        }
        if (XpNew.Text.Trim() is { Length: > 0 } xt)
        {
            if (!long.TryParse(xt, out var xp) || xp < 0 || xp > 999_999_999) { Say("经验填 0 或正整数"); return; }
            SendPlayerOp(new PlayerOp(0, "xp", Value: xp), $"经验改为 {xp}");
            sent = true;
        }
        if (!sent) Say("填写要改成的等级或经验");
    }

    // Called with every new telemetry sample.
    private void ShowPlayer()
    {
        if (_last?.Player is not { } p)
        {
            PlayerInfo.Text = _last is null ? "游戏未连接" : "游戏内模块不是 1.0.3，玩家工具不可用（请在“状态”页安装 / 更新）";
            return;
        }
        PlayerInfo.Text = p.Valid
            ? $"地图 {p.World}　位置 ({p.X / 100:0}, {p.Y / 100:0}, {p.Z / 100:0}) 米　游戏速度 {p.GameSpeed:0.##}　{(p.Fly ? "飞行中" : "")}"
            : "还没进入角色";
        LevelInfo.Text = $"当前等级 {p.Level?.ToString("0") ?? "—"}，经验 {p.Xp?.ToString("0") ?? "—"}";
        foreach (var r in p.Results.Where(r => r.Seq > _lastPlayerResultSeq && _pendingPlayerOps.Remove(r.Seq)))
        {
            Journal($"player op {r.Seq} {r.Kind} ok={r.Ok} error={r.Error} before={r.Before} after={r.After}");
            if (r.Kind == "teleport" && r.Ok) _lastTeleport = r;
            if (!r.Ok) Say(r.Error switch
            {
                "blocked" => "传送没有完成（目标处可能被挡住）",
                "incompatible game build" => "游戏版本未经验证，已停用",
                var other => "操作失败：" + other,
            });
        }
        if (p.Results.Count > 0) _lastPlayerResultSeq = Math.Max(_lastPlayerResultSeq, p.Results.Max(r => r.Seq));
    }

    // Hotkeys of this page; returns false for actions it does not own.
    private bool OnPlayerHotkey(string action)
    {
        switch (action)
        {
            case LockActions.SavePos: SavePosition(); return true;
            case LockActions.Teleport: TeleportToSaved(); return true;
            case LockActions.Fly: SetFly(!_cheats.Fly); return true;
            case LockActions.FlyUp: Nudge(0, 200, "上升 2 米"); return true;
            case LockActions.FlyDown: Nudge(0, -200, "下降 2 米"); return true;
            default: return false;
        }
    }
}
