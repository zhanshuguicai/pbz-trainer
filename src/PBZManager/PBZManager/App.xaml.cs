using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PBZManager;

public partial class App : Application
{
    // Development aid: `PBZManager.exe --snapshot <file.png> [seconds] [--tab <index>]` renders the main window to a
    // PNG after the given delay (default 5 s) and exits, so the UI can be checked without screen access.
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var i = Array.IndexOf(e.Args, "--snapshot");
        if (i < 0 || i + 1 >= e.Args.Length) return;
        var path = Path.GetFullPath(e.Args[i + 1]);
        var delay = i + 2 < e.Args.Length && double.TryParse(e.Args[i + 2], out var s) ? s : 5;
        var t = Array.IndexOf(e.Args, "--tab");
        if (t >= 0 && t + 1 < e.Args.Length && int.TryParse(e.Args[t + 1], out var tab))
            Dispatcher.BeginInvoke(() => { if (MainWindow is MainWindow w) w.Tabs.SelectedIndex = tab; }, DispatcherPriority.Loaded);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(delay) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (MainWindow is { } w) SaveWindow(w, path);
            Shutdown();
        };
        timer.Start();
    }

    private static void SaveWindow(Window window, string path)
    {
        var content = (FrameworkElement)window.Content;
        var dpi = VisualTreeHelper.GetDpi(window);
        var bitmap = new RenderTargetBitmap(
            (int)(content.ActualWidth * dpi.DpiScaleX), (int)(content.ActualHeight * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        var white = new DrawingVisual();
        using (var dc = white.RenderOpen()) dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, content.ActualWidth, content.ActualHeight));
        bitmap.Render(white);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var fs = File.Create(path);
        encoder.Save(fs);
    }
}
