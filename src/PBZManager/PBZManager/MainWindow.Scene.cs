using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using PBZ.Core.Scene;
using PBZ.Core.Suite;

namespace PBZManager;

// One line of the scene list.
public sealed record SceneRow(SceneActor Actor)
{
    public string Name => Actor.DisplayName;
    public string Distance => $"{Actor.Distance / 100.0:0.0} 米";
    public string State => (Actor.Hidden ? "已隐藏" : "可见") + " · " + (Actor.Collision ? "挡路" : "可穿过");
    public string Detail => string.Join("  ", new[] { Actor.Known?.Hint, Actor.Flow is { } f ? "剧情 " + f : null, Actor.ShortName }
        .Where(s => !string.IsNullOrEmpty(s)));
}

// "场景修复" page: lists nearby mechanisms and dissolves / hides / restores one, for saves stuck behind one-time mechanisms.
public partial class MainWindow
{
    private readonly ObservableCollection<SceneRow> _sceneRows = [];
    private SceneSnapshot? _scene;
    private readonly HashSet<long> _pendingSceneOps = [];
    private DateTime _sceneRequestedAt = DateTime.MinValue;

    private void InitScene() => SceneGrid.ItemsSource = _sceneRows;

    private void ScanScene_Click(object sender, RoutedEventArgs e)
    {
        if (!ItemsReady(out var why)) { Say("无法扫描：" + why); return; }
        try
        {
            var current = SuiteConfigStore.Read(_game!.RuntimeDir);
            SuiteConfigStore.Write(_game.RuntimeDir, current with { SceneRequest = current.SceneRequest + 1 });
            _sceneRequestedAt = DateTime.UtcNow;
            SceneState.Text = "已请求，等待游戏回应…";
        }
        catch (IOException ex) { Say("写入请求失败：" + ex.Message); }
    }

    private void SceneDissolve_Click(object sender, RoutedEventArgs e) => SendSceneOp("dissolve");
    private void SceneRemove_Click(object sender, RoutedEventArgs e) => SendSceneOp("remove");
    private void SceneRestore_Click(object sender, RoutedEventArgs e) => SendSceneOp("restore");

    private void SendSceneOp(string kind)
    {
        var rows = SceneGrid.SelectedItems.OfType<SceneRow>().ToList();
        if (rows.Count == 0) { Say("先在列表里选中要处理的物件"); return; }
        if (!ItemsReady(out var why)) { Say("无法操作：" + why); return; }
        if (!_savesBackedUp && !BackupSaves()) return;
        try
        {
            var current = SuiteConfigStore.Read(_game!.RuntimeDir);
            var before = (current.SceneOps ?? []).Select(o => o.Seq).ToHashSet();
            var next = SceneEdits.Append(current, rows.Select(r => (kind, r.Actor.Path)));
            foreach (var op in next.SceneOps!.Where(o => !before.Contains(o.Seq))) _pendingSceneOps.Add(op.Seq);
            SuiteConfigStore.Write(_game.RuntimeDir, next);
            Say($"已发送 {rows.Count} 项场景操作，等待游戏回应…");
        }
        catch (IOException ex) { Say("写入操作失败：" + ex.Message); }
    }

    private void PollScene()
    {
        if (_game is null) return;
        var snap = SceneSnapshot.TryRead(_game.RuntimeDir);
        if (snap is null || (_scene is { } old && old.SessionId == snap.SessionId && old.Seq == snap.Seq))
        {
            if (_sceneRequestedAt != DateTime.MinValue && (DateTime.UtcNow - _sceneRequestedAt).TotalSeconds > 6)
            {
                _sceneRequestedAt = DateTime.MinValue;
                SceneState.Text = "游戏没有回应：游戏内模块可能是旧版，请关闭游戏后在“状态”页点“安装 / 更新”（需要 PBZSuite 1.0.3）";
            }
            return;
        }
        _scene = snap;
        _sceneRequestedAt = DateTime.MinValue;
        var selected = SceneGrid.SelectedItems.OfType<SceneRow>().Select(r => r.Actor.Path).ToHashSet();
        _sceneRows.Clear();
        foreach (var a in snap.Actors.OrderBy(a => a.Known is null).ThenBy(a => a.Distance)) _sceneRows.Add(new SceneRow(a));
        foreach (var row in _sceneRows.Where(r => selected.Contains(r.Actor.Path))) SceneGrid.SelectedItems.Add(row);
        SceneState.Text = snap.Valid
            ? $"{DateTime.Now:HH:mm:ss} 附近 20 米内找到 {snap.Actors.Count} 个物件（已认识的 {snap.Actors.Count(a => a.Known is not null)} 个排在最前）"
            : "游戏还没进入角色";
        var mine = snap.Results.Where(r => _pendingSceneOps.Remove(r.Seq)).ToList();
        if (mine.Count == 0) return;
        foreach (var r in mine) Journal($"scene op {r.Seq} {r.Kind} {r.Path} ok={r.Ok} hidden={r.Hidden} collision={r.Collision} dissolved={r.Dissolved} error={r.Error}");
        var failed = mine.Where(r => !r.Ok).ToList();
        if (failed.Count > 0)
            Say($"场景操作失败 {failed.Count} 项：" + failed[0].Error switch
            {
                "not found" => "物件已不在（离开了区域？请重新扫描）",
                "dissolved" => "溶解过的画不能变回来，读档或离开区域再回来即可恢复",
                var other => other,
            });
        else if (mine.Any(r => r.Dissolved == true))
            Say("已开始溶解，约 3 秒后可以穿过");
        else Say($"已完成 {mine.Count} 项场景操作");
    }
}
