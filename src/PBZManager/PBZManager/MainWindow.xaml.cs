using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using PBZ.Core.Events;
using PBZ.Core.Combat;
using PBZ.Core.Game;
using PBZ.Core.Items;
using PBZ.Core.Lock;
using PBZ.Core.Music;
using PBZ.Core.Suite;
using PBZ.Core.Telemetry;

namespace PBZManager;

public partial class MainWindow : Window
{
    private static readonly (string Dir, IReadOnlyList<string> MigratedFrom) Data = PBZ.Core.DataLocation.Prepare(AppContext.BaseDirectory, PBZ.Core.DataLocation.LegacyDirs());
    private static readonly string DataDir = Data.Dir;
    private static readonly string PayloadDir = Path.Combine(AppContext.BaseDirectory, "payload", "PBZSuite");
    private static readonly string SettingsFile = Path.Combine(DataDir, "settings.json");
    private static readonly string LockFile = Path.Combine(DataDir, "lock.json");
    // SUITE_VERSION of the PBZSuite shipped with this manager, to spot an older one still running in the game.
    private static readonly string? PayloadSuiteVersion = ReadPayloadVersion();
    private string? _warnedVersion;
    private DateTime _lastLogCheck;
    private string? _suiteScriptError;

    private static string? ReadPayloadVersion()
    {
        try
        {
            var lua = File.ReadAllText(Path.Combine(PayloadDir, "Scripts", "main.lua"));
            var m = System.Text.RegularExpressions.Regex.Match(lua, @"local SUITE_VERSION = ""([^""]+)""");
            return m.Success ? m.Groups[1].Value : null;
        }
        catch (IOException) { return null; }
    }

    // Samples older than this mean the game stopped writing (closed, crashed or loading).
    private const int StaleAfterMs = 2000;

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly EventEngine _engine = new();
    private readonly WpfAudioOutput _audio;
    private readonly ConditionPlayer _player;
    private readonly ObservableCollection<RuleRow> _rules = [];
    private MusicSettings _music = MusicSettings.Load(SettingsFile);
    private bool _loadingSettings;
    private GameInstall? _game;
    private SuiteInstaller? _installer;
    private TelemetryTailer? _tailer;
    private GameCompatibility.Result? _compat;
    private Snapshot? _last;
    private DateTime _lastSnapshotAt;
    private LockSettings _lock = LockSettings.Load(LockFile);
    private bool _lockMaster; // never saved: every start begins with the lock off
    private bool _parryMaster; // same rule for auto deflect / dodge
    private ParryRelay? _relay;
    private GlobalHotkeys? _hotkeys;
    private string? _capturing; // lock action whose hotkey is being recorded
    private readonly ObservableCollection<ItemRow> _items = [];
    private InventorySnapshot? _inventory;
    private DateTime _lastInventoryCheck;
    private bool _savesBackedUp;
    private HashSet<long> _pendingOps = [];

    public MainWindow()
    {
        InitializeComponent();
        _audio = new WpfAudioOutput(() => _music);
        _audio.Error += message => { Say(message); Journal("audio error " + message); };
        _audio.Changed += ShowNowPlaying;
        _player = new ConditionPlayer(_audio);
        _player.Problem += (rule, message) => Say($"规则\"{rule.Name}\"：{message}");
        TriggerColumn.ItemsSource = RuleRow.Triggers;
        ScopeColumn.ItemsSource = RuleRow.Scopes;
        StopColumn.ItemsSource = RuleRow.StopTriggers;
        ItemGrid.ItemsSource = _items;
        InitScene();
        InitPlayer();
        RuleGrid.ItemsSource = _rules;
        _rules.CollectionChanged += (_, _) => RulesChanged();
        ShowMusicSettings();
        SaveMusic(_music); // writes the rule format once, replacing a T4d-format file
        ShowLockSettings();
        Loaded += async (_, _) =>
        {
            _hotkeys = new GlobalHotkeys(this);
            _hotkeys.Pressed += OnHotkey;
            ApplyHotkeys();
            await StartAsync();
        };
        Closed += (_, _) =>
        {
            _audio.Stop();
            _lockMaster = false;
            PushLock();
            _parryMaster = false;
            PushParry();
            _cheats = PBZ.Core.Player.SuiteCheats.Neutral;
            PushCheats();
            _relay?.Dispose();
            _hotkeys?.Dispose();
        };
        _timer.Tick += (_, _) => Poll();
    }

    private async Task StartAsync()
    {
        var steam = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
        _game = GameInstall.Locate(steam?.Replace('/', '\\'));
        if (_game is null)
        {
            GamePath.Text = "没有找到影之刃零（Steam 应用 4115450）";
            GamePath.Foreground = Brush("Bad");
            return;
        }
        GamePath.Text = _game.Root;
        LoaderState.Text = _game.LoaderInstalled ? "已安装" : "未安装（需要先装 UE4SS 与 PBZ 适配配置）";
        LoaderState.Foreground = Brush(_game.LoaderInstalled ? "Good" : "Bad");

        _installer = new SuiteInstaller(PayloadDir, _game.SuiteDir, Path.Combine(DataDir, "pbzsuite-manifest.json"));
        if (_installer.GetState() == PBZ.Core.Suite.SuiteState.Foreign && _installer.TryAdopt())
            Say("已接管此前用脚本安装的 PBZSuite");
        RefreshSuiteState();
        if (Data.MigratedFrom.Count > 0)
        {
            Say("已把旧设置迁移到 " + DataDir);
            Journal("migrated data from " + string.Join("; ", Data.MigratedFrom));
        }

        _tailer = new TelemetryTailer(_game.RuntimeDir);
        _relay = new ParryRelay(_game.RuntimeDir, Path.Combine(DataDir, "parry-relay.log"));
        ApplyRelayKeys();
        _timer.Start();

        GameVersion.Text = $"Steam 版本 {_game.BuildId ?? "未知"}，正在校验 EXE…";
        try
        {
            _compat = await GameCompatibility.CheckAsync(_game, Path.Combine(DataDir, "exe-hash-cache.json"));
            GameVersion.Text = $"Steam 版本 {_game.BuildId ?? "未知"}，{_compat.Reason}";
            GameVersion.Foreground = Brush(_compat.Compatible ? "Good" : "Warn");
            SyncCompatibilityFlag();
        }
        catch (IOException ex)
        {
            GameVersion.Text = "EXE 校验失败：" + ex.Message;
            GameVersion.Foreground = Brush("Bad");
        }
        // Clears a lock, auto deflect or player tool left on by a previous run that did not close normally.
        PushLock();
        PushParry();
        PushCheats();
        ShowLockAvailability();
    }

    // The game-side suite must know whether write-capable modules may run (ADR 0001, section 5).
    private void SyncCompatibilityFlag()
    {
        if (_game is null || _compat is null || _installer?.GetState() is not (PBZ.Core.Suite.SuiteState.Installed or PBZ.Core.Suite.SuiteState.Outdated)) return;
        var current = SuiteConfigStore.Read(_game.RuntimeDir);
        if (current.Compatible == _compat.Compatible && current.Revision > 0) return;
        var written = SuiteConfigStore.Write(_game.RuntimeDir, current with { Compatible = _compat.Compatible });
        Say($"已写入配置 revision {written.Revision}（compatible={written.Compatible}）");
    }

    private void Poll()
    {
        if (_tailer is null) return;
        foreach (var s in _tailer.ReadNew())
        {
            _last = s;
            _lastSnapshotAt = DateTime.UtcNow;
            var events = _engine.Process(s);
            foreach (var e in events) AddEvent(e);
            _player.Handle(events, new RuleState(_engine.InBattle, s.Health.Ratio, s.Combat.Valid ? s.Combat.InCombat : null,
                s.Flask.Valid ? s.Flask.Total : null, _engine.BossActive, s.Boss?.Ratio));
        }
        ShowLive();
        if (_game is not null && (DateTime.UtcNow - _lastLogCheck).TotalSeconds >= 5)
        {
            _lastLogCheck = DateTime.UtcNow;
            var tail = PBZ.Core.Suite.SuiteLogCheck.ReadTail(Path.Combine(_game.Ue4ssDir, "UE4SS.log"));
            var err = tail is null ? null : PBZ.Core.Suite.SuiteLogCheck.LastStartError(tail);
            if (err != _suiteScriptError)
            {
                _suiteScriptError = err;
                if (err is not null)
                {
                    Say("游戏内模块启动时出错，部分功能不可用：" + err.Split('\n')[^1]);
                    Journal("suite script error: " + err.Replace('\n', ' '));
                }
            }
        }
        if ((DateTime.UtcNow - _lastInventoryCheck).TotalMilliseconds >= 500)
        {
            _lastInventoryCheck = DateTime.UtcNow;
            PollInventory();
            PollScene();
            ShowPlayer();
        }
    }

    private void ShowLive()
    {
        var age = (DateTime.UtcNow - _lastSnapshotAt).TotalMilliseconds;
        if (_last is null)
        {
            LinkState.Text = IsGameRunning() ? "游戏在运行，但还没有收到数据" : "游戏未运行";
            LinkState.Foreground = Brush("Muted");
            ShowLockGameState(double.MaxValue);
            return;
        }
        if (age > StaleAfterMs)
        {
            var running = IsGameRunning();
            LinkState.Text = running ? $"{age / 1000:F0} 秒没有新数据（可能在加载）" : "游戏已关闭";
            LinkState.Foreground = Brush("Warn");
            // A battle that never ends because the game closed must not leave music playing.
            if (!running && _audio.CurrentKey is not null) _audio.FadeOut();
        }
        else
        {
            LinkState.Text = _last.IsPlaying ? "实时" : "实时（尚未进入游戏角色）";
            LinkState.Foreground = Brush("Good");
            if (_suiteScriptError is { } se)
            {
                LinkState.Text += "——游戏内模块启动时出错，部分功能不可用（详情见 ue4ss/UE4SS.log）：" + se.Split('\n')[^1];
                LinkState.Foreground = Brush("Bad");
            }
            if (_last.SuiteVersion is { } running && PayloadSuiteVersion is { } shipped && running != shipped)
            {
                LinkState.Text += $"——游戏内模块是旧版本 {running}（本程序带的是 {shipped}），新功能不可用：请退出游戏后在本页点“安装 / 更新”";
                LinkState.Foreground = Brush("Warn");
                if (_warnedVersion != running)
                {
                    _warnedVersion = running;
                    Say($"游戏内模块是旧版本 {running}，请退出游戏后点“安装 / 更新”到 {shipped}");
                }
            }
        }

        var h = _last.Health;
        if (h.Ratio is { } ratio)
        {
            HealthBar.Value = ratio;
            HealthText.Text = $"{h.Current:F0} / {h.Max:F0}（{ratio:P0}）";
        }
        else
        {
            HealthBar.Value = 0;
            HealthText.Text = "未知";
        }

        var c = _last.Combat;
        var battle = _engine.InBattle ? $"战斗中（第 {_engine.BattleId} 场）" : "未在战斗";
        CombatText.Text = (c.Valid ? $"{battle}，交战计数 {c.InCombat}{(c.Dead > 0 ? "，已死亡" : "")}" : $"{battle}，状态未知") +
            (_last.Boss is { Active: true } b ? $"；Boss {b.Name}{(b.Ratio is { } br ? $" {br:P0}" : "")}{(b.Count > 1 ? $"（共 {b.Count} 个）" : "")}" : "");

        var f = _last.Flask;
        FlaskText.Text = f.Valid
            ? $"上槽 {Slot(f.Top)}　下槽 {Slot(f.Bottom)}　合计 {f.Total}"
            : "未知";
        ShowLockGameState(age);
        SessionText.Text = $"{_last.SessionId}　序号 {_last.Sequence}　角色第 {_last.PlayerGeneration} 代　配置 revision {_last.ConfigRevision}";
    }

    private static string Slot(FlaskSlot? s) => s is null ? "?" : s.Id == "None" ? "空" : $"{s.Id} ×{(s.Count?.ToString() ?? "?")}";

    private void AddEvent(EngineEvent e)
    {
        var text = e.Kind switch
        {
            EngineEventKind.BattleStarted => $"第 {e.BattleId} 场战斗开始",
            EngineEventKind.BattleEnded => $"第 {e.BattleId} 场战斗结束（{Reason(e.Reason)}）",
            EngineEventKind.HealthBelow => $"第 {e.BattleId} 场：血量跌破 {e.Threshold:P0}",
            EngineEventKind.FlasksEmpty => "药已喝空",
            EngineEventKind.Death => "阵亡",
            EngineEventKind.Respawn => "角色重生/读图",
            EngineEventKind.HealthAbove => $"血量回升到 {e.Threshold:P0}",
            EngineEventKind.BigHit => $"一次掉血超过 {e.Threshold:P0}",
            EngineEventKind.FlaskUsed => "喝药",
            EngineEventKind.FlasksRefilled => "补药",
            EngineEventKind.EnemiesAtLeast => $"第 {e.BattleId} 场：敌人达到 {e.Threshold:0} 个",
            EngineEventKind.BattleLong => $"第 {e.BattleId} 场：已持续 {e.Threshold:0} 秒",
            EngineEventKind.ShachiFull => "杀气满",
            EngineEventKind.UltimateReady => "绝技能量满",
            EngineEventKind.UltimateUsed => "放出绝技",
            EngineEventKind.SpiritFull => "影/魂点满",
            EngineEventKind.SpiritEmpty => "影/魂点用完",
            EngineEventKind.BossFightStarted => $"Boss 战开始：{e.Reason}",
            EngineEventKind.BossFightEnded => e.Reason == "defeated" ? "Boss 战结束（击败）" : "Boss 战结束",
            EngineEventKind.BossHealthBelow => $"Boss 血量跌破 {e.Threshold:P0}",
            EngineEventKind.BossDefeated => $"击败 Boss：{e.Reason}",
            _ => e.Kind.ToString(),
        };
        EventList.Items.Insert(0, $"{DateTime.Now:HH:mm:ss}  {text}");
        while (EventList.Items.Count > 200) EventList.Items.RemoveAt(EventList.Items.Count - 1);
        Journal($"event {e.Kind} battle={e.BattleId} seq={e.Sequence} gameMs={e.MonotonicMs} reason={e.Reason} threshold={e.Threshold}");
    }

    private static string Reason(string r) => r switch
    {
        "left combat" => "脱战",
        "death" => "阵亡",
        "player changed" => "角色重建",
        "session changed" => "游戏重启",
        _ => r,
    };

    // Plain-text record of events and playback, kept for checking a play session afterwards.
    private static void Journal(string line)
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            File.AppendAllText(Path.Combine(DataDir, "events.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {line}{Environment.NewLine}");
        }
        catch (IOException) { }
    }

    // ---------- install ----------

    private void RefreshSuiteState()
    {
        if (_installer is null) return;
        var state = _installer.GetState();
        (SuiteState.Text, var tone) = state switch
        {
            PBZ.Core.Suite.SuiteState.NotInstalled => ("未安装", "Warn"),
            PBZ.Core.Suite.SuiteState.Installed => ("已安装，与本程序版本一致", "Good"),
            PBZ.Core.Suite.SuiteState.Outdated => ("已安装，但有新版本可更新", "Warn"),
            PBZ.Core.Suite.SuiteState.Modified => ("游戏目录里的文件被改过，请人工检查", "Bad"),
            _ => ("存在来源不明的 PBZSuite 文件夹，请人工检查", "Bad"),
        };
        SuiteState.Foreground = Brush(tone);
        var loaderOk = _game?.LoaderInstalled == true;
        // A folder holding only this tool's own files (for example installed by another copy of the manager) can be
        // taken over after the user confirms; anything else stays for manual inspection.
        var takeOver = state is PBZ.Core.Suite.SuiteState.Modified or PBZ.Core.Suite.SuiteState.Foreign && _installer.OnlyOwnFiles();
        if (takeOver) SuiteState.Text += "（都是本工具的文件，可点“安装 / 更新”覆盖）";
        InstallButton.IsEnabled = loaderOk && (state is PBZ.Core.Suite.SuiteState.NotInstalled or PBZ.Core.Suite.SuiteState.Outdated || takeOver);
        UninstallButton.IsEnabled = state is PBZ.Core.Suite.SuiteState.Installed or PBZ.Core.Suite.SuiteState.Outdated;
        ShowLockAvailability();
    }

    private void InstallButton_Click(object sender, RoutedEventArgs e) => RunWhileGameClosed("安装", () =>
    {
        if (_installer!.GetState() is PBZ.Core.Suite.SuiteState.Modified or PBZ.Core.Suite.SuiteState.Foreign)
        {
            if (MessageBox.Show(this, "游戏目录里已有一份 PBZSuite（可能是别的修改器副本装的），内容和本程序的记录不一致。" + Environment.NewLine + Environment.NewLine +
                    "它只包含本工具自己的文件。用本程序自带的版本覆盖它吗？运行记录（runtime 里的文件）会保留。",
                    "覆盖安装", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return "已取消";
            _installer.TakeOver();
        }
        _installer.Install();
        SyncCompatibilityFlag();
        return "PBZSuite 已安装，下次启动游戏生效";
    });

    private void UninstallButton_Click(object sender, RoutedEventArgs e) => RunWhileGameClosed("卸载", () =>
    {
        var archive = _installer!.Uninstall(Path.Combine(DataDir, "archive"));
        return archive is null ? "PBZSuite 已卸载" : $"PBZSuite 已卸载，运行记录已存到 {archive}";
    });

    private void RunWhileGameClosed(string action, Func<string> work)
    {
        if (IsGameRunning())
        {
            MessageBox.Show(this, $"请先正常退出游戏再{action}。", "游戏正在运行", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try { Say(work()); }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message, $"{action}失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        RefreshSuiteState();
    }

    // ---------- condition player ----------

    private void ShowMusicSettings()
    {
        _loadingSettings = true;
        MusicEnabled.IsChecked = _music.Enabled;
        VolumeSlider.Value = _music.Volume;
        FadeSeconds.Text = (_music.FadeOutMs / 1000.0).ToString("0.#");
        foreach (var row in _rules) row.PropertyChanged -= RuleRow_PropertyChanged;
        _rules.Clear();
        foreach (var rule in _music.Rules ?? []) AddRow(new RuleRow(rule));
        _loadingSettings = false;
        ApplyRules();
    }

    private void AddRow(RuleRow row)
    {
        row.PropertyChanged += RuleRow_PropertyChanged;
        _rules.Add(row);
    }

    private void RuleRow_PropertyChanged(object? sender, PropertyChangedEventArgs e) => RulesChanged();

    private void RulesChanged()
    {
        if (_loadingSettings) return;
        SaveMusic(_music with { Rules = _rules.Select(r => r.ToRule()).ToList() });
    }

    private void SaveMusic(MusicSettings next)
    {
        _music = next;
        ApplyRules();
        try { next.Save(SettingsFile); }
        catch (IOException ex) { Say("保存设置失败：" + ex.Message); }
    }

    private void ApplyRules()
    {
        _player.Enabled = _music.Enabled;
        _player.Rules = _music.Rules ?? [];
        _engine.Watch = _player.Watch;
    }

    private void ShowNowPlaying()
    {
        var rule = _rules.FirstOrDefault(r => r.Id == _audio.CurrentKey);
        NowPlaying.Text = _audio.CurrentKey switch
        {
            null => "未在播放",
            "preview" => "试听：" + Path.GetFileName(_audio.CurrentPath),
            _ => $"正在播放：{rule?.Name ?? "?"}（{Path.GetFileName(_audio.CurrentPath)}）",
        };
        Journal("audio " + (_audio.CurrentKey ?? "stopped/fading"));
    }

    private void MusicEnabled_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings) return;
        SaveMusic(_music with { Enabled = MusicEnabled.IsChecked == true });
        if (!_music.Enabled) _audio.FadeOut();
        Say(_music.Enabled ? "条件播放器已启用" : "条件播放器已关闭");
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loadingSettings) return;
        SaveMusic(_music with { Volume = Math.Round(VolumeSlider.Value, 2) });
        _audio.ApplyVolume();
    }

    private void FadeSeconds_LostFocus(object sender, RoutedEventArgs e)
    {
        if (double.TryParse(FadeSeconds.Text, out var seconds)) SaveMusic(_music with { FadeOutMs = (int)Math.Clamp(seconds * 1000, 0, 10_000) });
        FadeSeconds.Text = (_music.FadeOutMs / 1000.0).ToString("0.#");
    }

    private void AddRule_Click(object sender, RoutedEventArgs e)
    {
        var row = new RuleRow(PlayRule.New(RuleTrigger.BattleStarted));
        AddRow(row);
        RuleGrid.SelectedItem = row;
        RuleGrid.ScrollIntoView(row);
    }

    private void DeleteRule_Click(object sender, RoutedEventArgs e)
    {
        if (RuleGrid.SelectedItem is not RuleRow row) return;
        row.PropertyChanged -= RuleRow_PropertyChanged;
        _rules.Remove(row);
    }

    private void PreviewRule_Click(object sender, RoutedEventArgs e)
    {
        if (RuleGrid.SelectedItem is not RuleRow row) { Say("先在表格里选中一条规则"); return; }
        if (string.IsNullOrWhiteSpace(row.TrackPath)) { Say($"规则\"{row.Name}\"还没有选择歌曲"); return; }
        _audio.Play("preview", ConditionPlayer.Request(row.ToRule() with { StopTrigger = null, Playback = row.ToRule().How with { Loop = LoopMode.Once } }, row.TrackPath));
    }

    private void StopMusic_Click(object sender, RoutedEventArgs e) => _audio.FadeOut();

    private void ResetRules_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "用默认的\"半血 aizo\"和\"药喝空 雨爱 DJ\"两条规则替换当前全部规则？", "恢复默认",
                MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        var half = _rules.FirstOrDefault(r => r.Id == "half50")?.TrackPath;
        var flasks = _rules.FirstOrDefault(r => r.Id == "flasks0")?.TrackPath;
        SaveMusic(_music with { Rules = MusicSettings.DefaultRules(half, flasks) });
        ShowMusicSettings();
    }

    private void EditConditions_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not RuleRow row) return;
        var dialog = new ConditionsDialog(row.Name, row.Conditions) { Owner = this };
        if (dialog.ShowDialog() == true) row.Conditions = dialog.Result;
    }

    private void PickRuleTrack_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not RuleRow row) return;
        var dialog = new OpenFileDialog
        {
            Title = $"为\"{row.Name}\"选择歌曲（可多选，多首时触发时随机或轮流）",
            Filter = "音频文件|*.mp3;*.wav;*.wma;*.m4a;*.aac;*.flac|所有文件|*.*",
            FileName = row.TrackPath ?? "",
            Multiselect = true,
        };
        if (dialog.ShowDialog(this) == true) row.Tracks = dialog.FileNames;
    }

    private void EditPlayback_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not RuleRow row) return;
        var dialog = new PlaybackDialog(row.Name, row.Tracks, row.Playback) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        row.Tracks = dialog.Tracks.ToList();
        row.Playback = dialog.Result;
    }

    private void ExportRules_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Title = "导出条件播放器规则", Filter = "规则文件|*.json", FileName = "条件播放器规则.json" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            new MusicSettings(_music.Enabled, _music.Volume, _music.FadeOutMs, _rules.Select(r => r.ToRule()).ToList()).Save(dialog.FileName);
            Say("规则已导出到 " + dialog.FileName);
        }
        catch (IOException ex) { Say("导出失败：" + ex.Message); }
    }

    private void ImportRules_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "导入条件播放器规则", Filter = "规则文件|*.json|所有文件|*.*" };
        if (dialog.ShowDialog(this) != true) return;
        var imported = MusicSettings.Load(dialog.FileName);
        if (imported.Rules is not { Count: > 0 } rules) { Say("这个文件里没有规则"); return; }
        if (MessageBox.Show(this, $"用文件里的 {rules.Count} 条规则替换当前全部规则？（音量等总设置不变）", "导入规则",
                MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        SaveMusic(_music with { Rules = rules });
        ShowMusicSettings();
        var missing = rules.SelectMany(r => r.AllTracks).Count(t => !File.Exists(t));
        Say($"已导入 {rules.Count} 条规则" + (missing > 0 ? $"，其中 {missing} 首歌在本机找不到，请重新选择" : ""));
    }

    // ---------- lock ----------

    private void ShowLockSettings()
    {
        _loadingSettings = true;
        LockHealth.IsChecked = _lock.Health;
        LockShachi.IsChecked = _lock.Shachi;
        LockUltimate.IsChecked = _lock.Ultimate;
        LockSpirit.IsChecked = _lock.Spirit;
        LockSound.IsChecked = _lock.Sound;
        LockMaster.IsChecked = _lockMaster;
        // Inside the loading guard: setting a checkbox fires its Changed handler, which would save a half-loaded state.
        ParryMaster.IsChecked = _parryMaster;
        ParryDeflect.IsChecked = _lock.ParryDeflect;
        ParryDodge.IsChecked = _lock.ParryDodge;
        ParryComboHold.IsChecked = _lock.ParryComboHold;
        BuildParryTypePanel();
        ParryLead.Text = _lock.ParryLeadMs.ToString();
        ParryDeflectKey.ItemsSource = PressKey.All;
        ParryDodgeKey.ItemsSource = PressKey.All;
        ParryDeflectKey.SelectedValue = PressKey.Find(_lock.ParryDeflectKey, "RightMouse").Id;
        ParryDodgeKey.SelectedValue = PressKey.Find(_lock.ParryDodgeKey, "LeftShift").Id;
        _loadingSettings = false;
        ShowHotkeys();
        foreach (var a in LockActions.Items) ShowValue(a);
    }

    private TextBox ValueBox(string action) => action switch
    {
        LockActions.Health => ValueHealth,
        LockActions.Shachi => ValueShachi,
        LockActions.Ultimate => ValueUltimate,
        _ => ValueSpirit,
    };

    private void ShowValue(string action)
    {
        var box = ValueBox(action);
        box.Text = _lock.Value(action)?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "";
        box.ToolTip = "留空 = 锁满；填数字 = 固定为这个数";
    }

    private void Value_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox box) CommitValue(box);
    }

    private void Value_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox box) CommitValue(box);
    }

    private void CommitValue(TextBox box)
    {
        if (box.Tag is not string action) return;
        if (!LockSettings.TryParseValue(action, box.Text, out var value, out var error))
        {
            Say($"{LockActions.Name(action)}：{error}");
            ShowValue(action);
            return;
        }
        if (value != _lock.Value(action))
        {
            SaveLock(_lock.WithValue(action, value));
            PushLock();
            Say($"{LockActions.Name(action)}改为{(value is { } v ? $"固定 {v}" : "锁满")}");
        }
        ShowValue(action);
    }

    private Button HotkeyButton(string action) => action switch
    {
        LockActions.Toggle => HotkeyToggle,
        LockActions.Health => HotkeyHealth,
        LockActions.Shachi => HotkeyShachi,
        LockActions.Ultimate => HotkeyUltimate,
        LockActions.Parry => HotkeyParry,
        LockActions.MusicStop => HotkeyMusicStop,
        LockActions.MusicToggle => HotkeyMusicToggle,
        LockActions.SavePos => HotkeySavePos,
        LockActions.Teleport => HotkeyTeleport,
        LockActions.Fly => HotkeyFly,
        LockActions.FlyUp => HotkeyFlyUp,
        LockActions.FlyDown => HotkeyFlyDown,
        _ => HotkeySpirit,
    };

    private CheckBox LockBox(string action) => action switch
    {
        LockActions.Health => LockHealth,
        LockActions.Shachi => LockShachi,
        LockActions.Ultimate => LockUltimate,
        _ => LockSpirit,
    };

    private void ShowHotkeys()
    {
        foreach (var a in LockActions.All)
        {
            var b = HotkeyButton(a);
            b.Content = a == _capturing ? "请按键…" : Hotkey.Parse(_lock.HotkeyText(a))?.Display ?? "未设热键";
            b.ToolTip = $"“{LockActions.Name(a)}”的热键，点击修改";
        }
    }

    private void ApplyHotkeys()
    {
        if (_hotkeys is null) return;
        var failed = _hotkeys.Apply(LockActions.All.Select(a => (a, Hotkey.Parse(_lock.HotkeyText(a)))));
        foreach (var a in LockActions.All) HotkeyButton(a).Foreground = failed.Contains(a) ? Brush("Bad") : Brush("Ink");
        if (failed.Count > 0)
            Say("这些热键注册失败（可能被其他程序占用），请换一个：" + string.Join("、", failed.Select(LockActions.Name)));
    }

    private void SaveLock(LockSettings next)
    {
        _lock = next;
        try { next.Save(LockFile); }
        catch (IOException ex) { Say("保存锁定设置失败：" + ex.Message); }
    }

    private void LockItem_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings) return;
        SaveLock(_lock with
        {
            Health = LockHealth.IsChecked == true,
            Shachi = LockShachi.IsChecked == true,
            Ultimate = LockUltimate.IsChecked == true,
            Spirit = LockSpirit.IsChecked == true,
        });
        PushLock();
    }

    private void LockSound_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings) return;
        SaveLock(_lock with { Sound = LockSound.IsChecked == true });
    }

    private void LockMaster_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings) return;
        SetMaster(LockMaster.IsChecked == true, fromHotkey: false);
    }

    private void SetMaster(bool on, bool fromHotkey)
    {
        if (on && LockBlockedReason() is { } why)
        {
            Say("无法开启锁定：" + why);
            on = false;
        }
        _lockMaster = on;
        _loadingSettings = true;
        LockMaster.IsChecked = on;
        _loadingSettings = false;
        PushLock();
        Say(on ? "锁定已开启" : "锁定已关闭");
        if (fromHotkey) Cue(on);
    }

    // Null when the lock may be turned on.
    private string? LockBlockedReason()
    {
        if (_game is null) return "没有找到游戏";
        if (_compat is null) return "正在校验游戏版本，请稍候";
        if (!_compat.Compatible) return "游戏版本未通过校验，写入类功能已停用";
        if (_installer?.GetState() is not (PBZ.Core.Suite.SuiteState.Installed or PBZ.Core.Suite.SuiteState.Outdated)) return "PBZSuite 未安装";
        return null;
    }

    private void ShowLockAvailability()
    {
        var why = LockBlockedReason();
        LockBlocked.Text = why is null ? "" : "暂不可用：" + why;
        LockBlocked.Visibility = why is null ? Visibility.Collapsed : Visibility.Visible;
        ParryBlocked.Text = LockBlocked.Text;
        ParryBlocked.Visibility = LockBlocked.Visibility;
    }

    // Writes the lock switches into runtime/config.json; the game applies them within about half a second.
    private void PushLock()
    {
        if (_game is null || _installer?.GetState() is not (PBZ.Core.Suite.SuiteState.Installed or PBZ.Core.Suite.SuiteState.Outdated)) return;
        double V(string a) => _lock.Value(a) ?? SuiteLock.FullValue;
        var wanted = new SuiteLock(_lockMaster, _lock.Health, _lock.Shachi, _lock.Ultimate, _lock.Spirit,
            V(LockActions.Health), V(LockActions.Shachi), V(LockActions.Ultimate), V(LockActions.Spirit));
        try
        {
            var current = SuiteConfigStore.Read(_game.RuntimeDir);
            if (current.Lock == wanted) return;
            var written = SuiteConfigStore.Write(_game.RuntimeDir, current with { Lock = wanted });
            Journal($"lock revision={written.Revision} enabled={wanted.Enabled} hp={wanted.Health} shachi={wanted.Shachi} ult={wanted.Ultimate} spirit={wanted.Spirit} values={wanted.HealthValue}/{wanted.ShachiValue}/{wanted.UltimateValue}/{wanted.SpiritValue}");
        }
        catch (IOException ex) { Say("写入锁定配置失败：" + ex.Message); }
    }

    private void ShowLiveValues(double age)
    {
        var fresh = _last is not null && age <= StaleAfterMs;
        var v = _last?.Values ?? ValuesSample.Unknown;
        LiveHealth.Text = fresh ? Pair(_last!.Health) : "—";
        LiveShachi.Text = fresh ? Pair(v.Shachi) : "—";
        LiveUltimate.Text = fresh ? Pair(v.Ultimate) : "—";
        LiveSpirit.Text = fresh ? Pair(v.Spirit) : "—";
    }

    private static string Pair(HealthSample s) => s.Valid && s.Current is { } c && s.Max is { } m ? $"{c:0.#} / {m:0.#}" : "未知";

    private void ShowLockGameState(double age)
    {
        ShowLiveValues(age);
        ShowParryState(age);
        if (age > StaleAfterMs)
        {
            LockGameState.Text = "游戏未连接";
            LockGameState.Foreground = Brush("Muted");
        }
        else if (_last?.Modules.Lock == true)
        {
            LockGameState.Text = "游戏内已生效";
            LockGameState.Foreground = Brush("Good");
        }
        else
        {
            LockGameState.Text = _lockMaster ? "等待游戏应用…" : "游戏内未启用";
            LockGameState.Foreground = Brush(_lockMaster ? "Warn" : "Muted");
        }
    }

    // ---------- auto deflect / dodge (T5b) ----------

    private void ParryMaster_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings) return;
        SetParry(ParryMaster.IsChecked == true, fromHotkey: false);
    }

    private void SetParry(bool on, bool fromHotkey)
    {
        if (on && LockBlockedReason() is { } why)
        {
            Say("无法开启自动弹反：" + why);
            on = false;
        }
        _parryMaster = on;
        if (_relay is not null) _relay.Enabled = on;
        _loadingSettings = true;
        ParryMaster.IsChecked = on;
        _loadingSettings = false;
        PushParry();
        Say(on ? "自动弹反/闪避已开启" : "自动弹反/闪避已关闭");
        if (fromHotkey) Cue(on);
    }

    private void ParryOption_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings) return;
        SaveLock(_lock with { ParryDeflect = ParryDeflect.IsChecked == true, ParryDodge = ParryDodge.IsChecked == true, ParryComboHold = ParryComboHold.IsChecked == true });
        ApplyRelayKeys();
        PushParry();
    }

    // One drop-down per attack type: what to do when that kind of attack comes in.
    private void BuildParryTypePanel()
    {
        ParryTypePanel.Children.Clear();
        foreach (var (id, name, defaultAction) in AttackTypes.All)
        {
            var current = _lock.ParryActions is { } map && map.TryGetValue(id, out var a) ? a : defaultAction;
            var box = new ComboBox { Width = 150, Tag = id, ItemsSource = AttackTypes.Actions.Select(x => x.Name).ToList() };
            box.SelectedIndex = Math.Max(0, AttackTypes.Actions.ToList().FindIndex(x => x.Action == current));
            box.SelectionChanged += ParryType_Changed;
            var cell = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 18, 6) };
            cell.Children.Add(new TextBlock { Text = name, Width = 170, VerticalAlignment = VerticalAlignment.Center, Foreground = Brush("Muted") });
            cell.Children.Add(box);
            ParryTypePanel.Children.Add(cell);
        }
    }

    private void ParryType_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingSettings || sender is not ComboBox { Tag: string id } box || box.SelectedIndex < 0) return;
        var map = new Dictionary<string, string>(_lock.ParryActions ?? []) { [id] = AttackTypes.Actions[box.SelectedIndex].Action };
        SaveLock(_lock with { ParryActions = map });
        PushParry();
        Say($"攻击类型 {AttackTypes.All.First(t => t.Id == id).Name} 的应对改为 {AttackTypes.Actions[box.SelectedIndex].Name}");
    }

    private void ParryKey_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingSettings) return;
        SaveLock(_lock with
        {
            ParryDeflectKey = ParryDeflectKey.SelectedValue as string ?? "RightMouse",
            ParryDodgeKey = ParryDodgeKey.SelectedValue as string ?? "LeftShift",
        });
        ApplyRelayKeys();
    }

    private void ApplyRelayKeys()
    {
        if (_relay is null) return;
        _relay.DeflectKey = PressKey.Find(_lock.ParryDeflectKey, "RightMouse");
        _relay.DodgeKey = PressKey.Find(_lock.ParryDodgeKey, "LeftShift");
        _relay.ComboHold = _lock.ParryComboHold;
    }

    private void ParryLead_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) CommitParryLead();
    }

    private void ParryLead_LostFocus(object sender, RoutedEventArgs e) => CommitParryLead();

    private void CommitParryLead()
    {
        if (int.TryParse(ParryLead.Text.Trim(), out var ms))
        {
            ms = Math.Clamp(ms, 0, SuiteParry.MaxLeadMs);
            if (ms != _lock.ParryLeadMs)
            {
                SaveLock(_lock with { ParryLeadMs = ms });
                PushParry();
                Say($"提前量改为 {ms} 毫秒");
            }
        }
        else Say("提前量需要 0–300 的整数");
        ParryLead.Text = _lock.ParryLeadMs.ToString();
    }

    private void PushParry()
    {
        if (_game is null || _installer?.GetState() is not (PBZ.Core.Suite.SuiteState.Installed or PBZ.Core.Suite.SuiteState.Outdated)) return;
        var wanted = new SuiteParry(_lock.ParryDeflect, _lock.ParryDodge, _lock.ParryLeadMs, _lock.ParryActions is { Count: > 0 } a ? new(a) : null);
        try
        {
            var current = SuiteConfigStore.Read(_game.RuntimeDir);
            if (current.Modules.Parry == _parryMaster && wanted.SameAs(current.Parry)) return;
            var written = SuiteConfigStore.Write(_game.RuntimeDir, current with { Modules = current.Modules with { Parry = _parryMaster }, Parry = wanted });
            Journal($"parry revision={written.Revision} enabled={_parryMaster} deflect={wanted.Deflect} dodge={wanted.Dodge} lead={wanted.LeadMs}");
        }
        catch (IOException ex) { Say("写入自动弹反配置失败：" + ex.Message); }
    }

    private void ShowParryState(double age)
    {
        if (age > StaleAfterMs || _last is null)
        {
            ParryState.Text = "游戏未连接";
            ParryState.Foreground = Brush("Muted");
            return;
        }
        if (_last.Modules.Parry)
        {
            ParryState.Text = "游戏内已生效";
            ParryState.Foreground = Brush("Good");
        }
        else
        {
            ParryState.Text = _parryMaster ? "等待游戏应用…（需要 PBZSuite 1.0.2）" : "游戏内未启用";
            ParryState.Foreground = Brush(_parryMaster ? "Warn" : "Muted");
        }
        if (_last.Parry is { } p)
        {
            var total = p.HitsTaken + p.Clean;
            ParryStats.Text = $"本局：弹反 {p.Deflects} 次，闪避 {p.Dodges} 次；已判定 {total} 次，其中受击 {p.HitsTaken} 次" +
                (total > 0 ? $"（未受击 {100.0 * p.Clean / total:0}%）" : "") +
                (_relay is { } r ? $"；本程序已按键 {r.Presses} 次" + (r.SkippedNotForeground > 0 ? $"，游戏不在前台跳过 {r.SkippedNotForeground} 次" : "") : "");
        }
    }

    private void OnHotkey(string action)
    {
        if (_capturing is not null) return;
        if (OnPlayerHotkey(action)) return;
        if (action == LockActions.MusicStop)
        {
            _audio.FadeOut();
            Say("已停止当前歌曲");
            return;
        }
        if (action == LockActions.MusicToggle)
        {
            MusicEnabled.IsChecked = MusicEnabled.IsChecked != true; // runs MusicEnabled_Changed
            Cue(MusicEnabled.IsChecked == true);
            return;
        }
        if (action == LockActions.Parry)
        {
            SetParry(!_parryMaster, fromHotkey: true);
            return;
        }
        if (action == LockActions.Toggle)
        {
            SetMaster(!_lockMaster, fromHotkey: true);
            return;
        }
        var box = LockBox(action);
        var on = box.IsChecked != true;
        box.IsChecked = on; // runs LockItem_Changed, which saves and pushes
        Say($"{LockActions.Name(action)}锁定：{(on ? "开" : "关")}{(_lockMaster ? "" : "（总开关未开）")}");
        Cue(on);
    }

    // Short rising / falling tones so a hotkey press is confirmed while the game is in full screen.
    private void Cue(bool on)
    {
        if (!_lock.Sound) return;
        Task.Run(() =>
        {
            try
            {
                Console.Beep(on ? 880 : 1320, 80);
                Console.Beep(on ? 1320 : 660, 110);
            }
            catch (Exception ex) when (ex is IOException or PlatformNotSupportedException or InvalidOperationException) { }
        });
    }

    private void Hotkey_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string action }) return;
        _capturing = action;
        _hotkeys?.Clear(); // otherwise pressing an existing combination would fire it
        ShowHotkeys();
        Say($"请按下“{LockActions.Name(action)}”的新热键（Esc 取消，Backspace 清除）");
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_capturing is null) return;
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin or Key.None) return; // wait for the main key
        if (key == Key.Escape) { EndCapture(null); return; }
        if (key is Key.Back or Key.Delete) { EndCapture(""); return; }

        var mods = HotkeyModifiers.None;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) mods |= HotkeyModifiers.Ctrl;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) mods |= HotkeyModifiers.Alt;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) mods |= HotkeyModifiers.Shift;
        var standalone = key is >= Key.F1 and <= Key.F24 or >= Key.NumPad0 and <= Key.Divide;
        if (mods == HotkeyModifiers.None && !standalone)
        {
            Say("单个字母、数字等键会影响打字，请配合 Ctrl / Alt / Shift 使用");
            return;
        }
        var hk = new Hotkey(mods, key.ToString());
        var clash = LockActions.All.FirstOrDefault(a => a != _capturing && Hotkey.Parse(_lock.HotkeyText(a))?.ToString() == hk.ToString());
        if (clash is not null)
        {
            Say($"{hk.Display} 已经用于“{LockActions.Name(clash)}”，请换一个");
            return;
        }
        EndCapture(hk.ToString());
    }

    // text: null = cancel, "" = clear, otherwise the new hotkey.
    private void EndCapture(string? text)
    {
        var action = _capturing!;
        _capturing = null;
        if (text is not null)
        {
            SaveLock(_lock.WithHotkey(action, text));
            Say(text == "" ? $"已清除“{LockActions.Name(action)}”的热键" : $"“{LockActions.Name(action)}”的热键改为 {Hotkey.Parse(text)!.Display}");
        }
        ApplyHotkeys();
        ShowHotkeys();
    }


    // ---------- items (T5a) ----------

    private bool ItemsReady(out string why)
    {
        why = LockBlockedReason() ?? "";
        if (why.Length > 0) return false;
        if (_last is null || (DateTime.UtcNow - _lastSnapshotAt).TotalMilliseconds > StaleAfterMs || !_last.IsPlaying)
        {
            why = "游戏没有在运行，或还没进入角色";
            return false;
        }
        return true;
    }

    private void ReadItems_Click(object sender, RoutedEventArgs e)
    {
        if (!ItemsReady(out var why)) { Say("无法读取物品：" + why); return; }
        try
        {
            var current = SuiteConfigStore.Read(_game!.RuntimeDir);
            SuiteConfigStore.Write(_game.RuntimeDir, current with { InventoryRequest = current.InventoryRequest + 1 });
            ItemsState.Text = "已请求，等待游戏回应…";
        }
        catch (IOException ex) { Say("写入请求失败：" + ex.Message); }
    }

    private void PollInventory()
    {
        if (_game is null) return;
        var snap = InventorySnapshot.TryRead(_game.RuntimeDir);
        if (snap is null || (_inventory is { } old && old.SessionId == snap.SessionId && old.Seq == snap.Seq)) return;
        _inventory = snap;
        MoneyText.Text = snap.Money?.ToString("N0") ?? "未知";
        ShowItems();
        ItemsState.Text = $"{DateTime.Now:HH:mm:ss} 读取到 {snap.Items.Count} 项（可修改 {snap.Items.Count(i => i.Editable)} 项）";
        ReportResults(snap);
    }

    private void ShowItems()
    {
        if (_inventory is null) return;
        var typed = _items.Where(r => r.NewAmount.Length > 0).ToDictionary(r => r.Id, r => r.NewAmount);
        _items.Clear();
        foreach (var item in _inventory.Items.OrderBy(i => i.SortKey).ThenBy(i => i.Id, StringComparer.Ordinal))
        {
            if (EditableOnly.IsChecked == true && !item.Editable) continue;
            var row = new ItemRow(item);
            // Keep what the user typed across a refresh, unless that edit was just applied.
            if (typed.TryGetValue(item.Id, out var t) && t != item.Amount?.ToString()) row.NewAmount = t;
            _items.Add(row);
        }
    }

    private void ReportResults(InventorySnapshot snap)
    {
        var mine = snap.Results.Where(r => _pendingOps.Remove(r.Seq)).ToList();
        if (mine.Count == 0) return;
        var failed = mine.Where(r => !r.Ok).ToList();
        foreach (var r in mine) Journal($"item op {r.Seq} {r.Kind} {r.Id} {r.Before}->{r.After} target={r.Target} ok={r.Ok} error={r.Error}");
        if (r0(failed) is { } f)
            Say($"已修改 {mine.Count - failed.Count} 项，失败 {failed.Count} 项（例如 {f.Id ?? "钱"}：{f.Error ?? $"结果 {f.After}"}）");
        else Say($"已修改 {mine.Count} 项，游戏回读一致");
        if (_pendingOps.Count == 0) MoneyNew.Text = "";

        static ItemOpResult? r0(List<ItemOpResult> l) => l.Count > 0 ? l[0] : null;
    }

    private void EditableOnly_Changed(object sender, RoutedEventArgs e) => ShowItems();

    private void Fill999_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in ItemGrid.SelectedItems.OfType<ItemRow>().Where(r => r.Editable)) row.NewAmount = "999";
    }

    private void ClearItemInputs_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in _items) row.NewAmount = "";
    }

    private void ApplyItems_Click(object sender, RoutedEventArgs e)
    {
        List<(string, string?, long)> edits;
        try { edits = _items.Select(r => (r, t: r.Target())).Where(x => x.t is not null).Select(x => ("item", (string?)x.r.Id, x.t!.Value)).ToList(); }
        catch (FormatException ex) { Say(ex.Message); return; }
        if (edits.Count == 0) { Say("没有要修改的数量"); return; }
        SendItemEdits(edits);
    }

    private void ApplyMoney_Click(object sender, RoutedEventArgs e)
    {
        if (!long.TryParse(MoneyNew.Text.Trim(), out var v) || v < 0) { Say("钱需要填 0 或正整数"); return; }
        SendItemEdits([("money", null, Math.Min(v, InventorySnapshot.MoneyMax))]);
    }

    private void SendItemEdits(List<(string Kind, string? Id, long Target)> edits)
    {
        if (!ItemsReady(out var why)) { Say("无法修改：" + why); return; }
        if (!_savesBackedUp && !BackupSaves()) return;
        try
        {
            var current = SuiteConfigStore.Read(_game!.RuntimeDir);
            var before = (current.ItemOps ?? []).Select(o => o.Seq).ToHashSet();
            var next = ItemEdits.Append(current, edits);
            foreach (var op in next.ItemOps!.Where(o => !before.Contains(o.Seq))) _pendingOps.Add(op.Seq);
            SuiteConfigStore.Write(_game.RuntimeDir, next);
            Say($"已发送 {edits.Count} 项修改，等待游戏回读…");
        }
        catch (IOException ex) { Say("写入修改失败：" + ex.Message); }
    }

    // Copies the save folder once per manager run before the first item edit.
    private bool BackupSaves()
    {
        var saves = Path.Combine(_game!.Root, "ProjectPBZ", "Saved", "SaveGames");
        if (!Directory.Exists(saves))
        {
            Say("找不到存档目录，为安全起见不修改：" + saves);
            return false;
        }
        try
        {
            var dest = Path.Combine(DataDir, "save-backups", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            foreach (var file in Directory.GetFiles(saves, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(dest, Path.GetRelativePath(saves, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
            _savesBackedUp = true;
            Journal("saves backed up to " + dest);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Say("备份存档失败，为安全起见不修改：" + ex.Message);
            return false;
        }
    }


    private static bool IsGameRunning() => Process.GetProcessesByName("PBZ").Length > 0;

    private void Say(string text) => StatusLine.Text = $"{DateTime.Now:HH:mm:ss}  {text}";

    private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];
}
