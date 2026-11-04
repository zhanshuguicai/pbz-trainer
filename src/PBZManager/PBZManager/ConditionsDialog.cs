using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using PBZ.Core.Music;

namespace PBZManager;

// Small editor for a rule's extra conditions; built in code because it is a plain form.
public sealed class ConditionsDialog : Window
{
    private readonly ComboBox _battle = new() { Width = 160 };
    private readonly ComboBox _boss = new() { Width = 160 };
    private readonly (CheckBox Box, TextBox Text)[] _fields;

    public RuleConditions Result { get; private set; }

    public ConditionsDialog(string ruleName, RuleConditions current)
    {
        Title = $"附加条件：{ruleName}";
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new System.Windows.Media.FontFamily("Microsoft YaHei UI");
        FontSize = 13;
        Background = (System.Windows.Media.Brush)Application.Current.Resources["Bg"];
        Foreground = (System.Windows.Media.Brush)Application.Current.Resources["Ink"];
        Result = current;

        _battle.ItemsSource = new[] { "不限", "仅战斗中", "仅非战斗" };
        _battle.SelectedIndex = (int)current.Battle;
        _boss.ItemsSource = new[] { "不限", "仅 Boss 战中", "仅非 Boss 战" };
        _boss.SelectedIndex = (int)current.Boss;

        _fields =
        [
            Field("当前血量低于", "%", current.HealthBelowPercent),
            Field("当前血量高于", "%", current.HealthAbovePercent),
            Field("交战敌人至少", "个", current.EnemiesAtLeast),
            Field("剩余药次数至多", "次", current.FlasksAtMost),
            Field("Boss 血量低于", "%", current.BossHealthBelowPercent),
        ];

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = "触发时下列条件要全部满足，规则才会播放（淡出不受影响）。", Margin = new Thickness(0, 0, 0, 10) });
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        row.Children.Add(new TextBlock { Text = "战斗状态", Width = 130, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(_battle);
        panel.Children.Add(row);
        var bossRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        bossRow.Children.Add(new TextBlock { Text = "Boss 战状态", Width = 130, VerticalAlignment = VerticalAlignment.Center });
        bossRow.Children.Add(_boss);
        panel.Children.Add(bossRow);
        foreach (var (box, text) in _fields)
        {
            var r = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            box.Width = 130;
            box.VerticalAlignment = VerticalAlignment.Center;
            r.Children.Add(box);
            r.Children.Add(text);
            r.Children.Add(new TextBlock { Text = (string)text.Tag, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            panel.Children.Add(r);
        }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        var ok = new Button { Content = "确定", IsDefault = true, Padding = new Thickness(16, 3, 16, 3), Margin = new Thickness(0, 0, 8, 0) };
        var clear = new Button { Content = "全部清除", Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "取消", IsCancel = true, Padding = new Thickness(16, 3, 16, 3) };
        ok.Click += (_, _) => Accept();
        clear.Click += (_, _) =>
        {
            _battle.SelectedIndex = 0;
            _boss.SelectedIndex = 0;
            foreach (var (box, _) in _fields) box.IsChecked = false;
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(clear);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        Content = panel;
    }

    private static (CheckBox, TextBox) Field(string label, string unit, double? value)
    {
        var box = new CheckBox { Content = label, IsChecked = value is not null };
        var text = new TextBox
        {
            Width = 70,
            Text = value?.ToString("0.#", CultureInfo.InvariantCulture) ?? "",
            Tag = unit,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        text.TextChanged += (_, _) => { if (text.Text.Length > 0) box.IsChecked = true; };
        return (box, text);
    }

    private void Accept()
    {
        double? Read(int i, double min, double max)
        {
            var (box, text) = _fields[i];
            if (box.IsChecked != true) return null;
            if (!double.TryParse(text.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) throw new FormatException(((string)box.Content) + " 需要一个数字");
            return Math.Clamp(v, min, max);
        }
        try
        {
            Result = new RuleConditions(
                (BattleFilter)Math.Max(0, _battle.SelectedIndex),
                Read(0, 1, 100),
                Read(1, 0, 99),
                Read(2, 1, 20) is { } e ? (int)e : null,
                Read(3, 0, 99) is { } f ? (int)f : null,
                (BossFilter)Math.Max(0, _boss.SelectedIndex),
                Read(4, 1, 100));
            DialogResult = true;
        }
        catch (FormatException ex)
        {
            MessageBox.Show(this, ex.Message, "附加条件", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
