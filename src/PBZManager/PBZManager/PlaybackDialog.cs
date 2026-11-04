using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PBZ.Core.Music;

namespace PBZManager;

// Per-rule playback settings: the song list and how it plays. Built in code because it is a plain form.
public sealed class PlaybackDialog : Window
{
    private readonly ListBox _list = new() { Height = 110, Width = 420 };
    private readonly List<string> _tracks;
    private readonly CheckBox _shuffle = new() { Content = "多首时随机（不连着重复）；不勾则轮流", Margin = new Thickness(0, 4, 0, 8) };
    private readonly TextBox _volume = Box();
    private readonly TextBox _start = Box();
    private readonly ComboBox _loop = new() { Width = 220 };
    private readonly TextBox _fadeIn = Box();
    private readonly TextBox _fadeOut = Box();
    private readonly TextBox _cooldown = Box();

    public IReadOnlyList<string> Tracks => _tracks;
    public RulePlayback Result { get; private set; }

    public PlaybackDialog(string ruleName, IReadOnlyList<string> tracks, RulePlayback current)
    {
        Title = $"播放设置：{ruleName}";
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new System.Windows.Media.FontFamily("Microsoft YaHei UI");
        FontSize = 13;
        Background = (System.Windows.Media.Brush)Application.Current.Resources["Bg"];
        Foreground = (System.Windows.Media.Brush)Application.Current.Resources["Ink"];
        Result = current;
        _tracks = [.. tracks];
        RefreshList();

        _shuffle.IsChecked = current.Shuffle;
        _volume.Text = current.VolumePercent.ToString(CultureInfo.InvariantCulture);
        _start.Text = current.StartSeconds.ToString("0.#", CultureInfo.InvariantCulture);
        _loop.ItemsSource = new[] { "自动（有淡出条件就循环，否则放一遍）", "循环直到淡出", "只放一遍" };
        _loop.SelectedIndex = (int)current.Loop;
        _fadeIn.Text = current.FadeInSeconds.ToString("0.#", CultureInfo.InvariantCulture);
        _fadeOut.Text = current.FadeOutSeconds?.ToString("0.#", CultureInfo.InvariantCulture) ?? "";
        _cooldown.Text = current.CooldownSeconds.ToString("0.#", CultureInfo.InvariantCulture);

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = "歌曲（触发时从这里选一首）", Margin = new Thickness(0, 0, 0, 4) });
        panel.Children.Add(_list);
        var listButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        listButtons.Children.Add(Button("添加…", (_, _) => AddTracks()));
        listButtons.Children.Add(Button("移除选中", (_, _) => { if (_list.SelectedIndex >= 0) { _tracks.RemoveAt(_list.SelectedIndex); RefreshList(); } }));
        panel.Children.Add(listButtons);
        panel.Children.Add(_shuffle);
        panel.Children.Add(Row("音量", _volume, "%（相对总音量，0–200）"));
        panel.Children.Add(Row("从第几秒开始", _start, "秒（跳过前奏；循环时也从这里重来）"));
        var loopRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        loopRow.Children.Add(new TextBlock { Text = "播放方式", Width = 110, VerticalAlignment = VerticalAlignment.Center });
        loopRow.Children.Add(_loop);
        panel.Children.Add(loopRow);
        panel.Children.Add(Row("淡入", _fadeIn, "秒"));
        panel.Children.Add(Row("淡出", _fadeOut, "秒（留空 = 用总设置）"));
        panel.Children.Add(Row("冷却", _cooldown, "秒（这段时间内不再触发这条规则）"));

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        var ok = Button("确定", (_, _) => Accept());
        ok.IsDefault = true;
        var cancel = Button("取消", (_, _) => { });
        cancel.IsCancel = true;
        buttons.Children.Add(ok);
        buttons.Children.Add(Button("恢复默认", (_, _) => Load(RulePlayback.Default)));
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        Content = panel;
    }

    private void Load(RulePlayback p)
    {
        _shuffle.IsChecked = p.Shuffle;
        _volume.Text = p.VolumePercent.ToString(CultureInfo.InvariantCulture);
        _start.Text = "0";
        _loop.SelectedIndex = (int)p.Loop;
        _fadeIn.Text = p.FadeInSeconds.ToString("0.#", CultureInfo.InvariantCulture);
        _fadeOut.Text = "";
        _cooldown.Text = "0";
    }

    private void AddTracks()
    {
        var dialog = new OpenFileDialog
        {
            Title = "添加歌曲（可多选）",
            Filter = "音频文件|*.mp3;*.wav;*.wma;*.m4a;*.aac;*.flac|所有文件|*.*",
            Multiselect = true,
        };
        if (dialog.ShowDialog(this) != true) return;
        foreach (var f in dialog.FileNames)
            if (!_tracks.Contains(f, StringComparer.OrdinalIgnoreCase)) _tracks.Add(f);
        RefreshList();
    }

    private void RefreshList()
    {
        _list.ItemsSource = null;
        _list.ItemsSource = _tracks.Select(Path.GetFileName).ToList();
    }

    private void Accept()
    {
        static double Num(TextBox t, string what)
        {
            if (!double.TryParse(t.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) throw new FormatException(what + " 需要一个数字");
            return v;
        }
        try
        {
            Result = new RulePlayback(
                (int)Math.Round(Num(_volume, "音量")),
                Num(_start, "开始秒数"),
                (LoopMode)Math.Max(0, _loop.SelectedIndex),
                Num(_fadeIn, "淡入"),
                string.IsNullOrWhiteSpace(_fadeOut.Text) ? null : Num(_fadeOut, "淡出"),
                Num(_cooldown, "冷却"),
                _shuffle.IsChecked == true).Clamped();
            DialogResult = true;
        }
        catch (FormatException ex)
        {
            MessageBox.Show(this, ex.Message, "播放设置", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static TextBox Box() => new() { Width = 70, VerticalContentAlignment = VerticalAlignment.Center };

    private static StackPanel Row(string label, TextBox box, string unit)
    {
        var r = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        r.Children.Add(new TextBlock { Text = label, Width = 110, VerticalAlignment = VerticalAlignment.Center });
        r.Children.Add(box);
        r.Children.Add(new TextBlock { Text = unit, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        return r;
    }

    private static Button Button(string text, RoutedEventHandler click)
    {
        var b = new Button { Content = text, Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(0, 0, 8, 0) };
        b.Click += click;
        return b;
    }
}
