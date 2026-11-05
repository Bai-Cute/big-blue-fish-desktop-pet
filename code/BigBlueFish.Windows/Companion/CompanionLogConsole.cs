using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace BigBlueFish.Windows;
public partial class MainWindow
{
    private Action BuildLogConsole(StackPanel panel, Window owner, Func<bool> visible)
    {
        panel.Children.Add(new TextBlock { Text = $"本次运行的排障日志，保留最近{DiagnosticLog.Capacity}条。记录操作、状态、耗时和错误码；不包含截图、OCR、聊天或提示词正文，目录已匿名化。", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.SlateGray });
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 8) };
        var follow = new CheckBox { Content = "自动滚动", IsChecked = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
        var save = new Button { Content = "保存为 TXT…", Padding = new Thickness(14, 7, 14, 7) };
        toolbar.Children.Add(save); toolbar.Children.Add(follow); panel.Children.Add(toolbar);
        var count = new TextBlock { Foreground = Brushes.SlateGray, Margin = new Thickness(0, 0, 0, 8) }; panel.Children.Add(count);
        var console = new TextBox { Name = "LogConsoleText", IsReadOnly = true, AcceptsReturn = true, AcceptsTab = true,
            TextWrapping = TextWrapping.NoWrap, FontFamily = new FontFamily("Consolas, Microsoft YaHei UI"), FontSize = 12,
            Height = 410, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
        System.Windows.Automation.AutomationProperties.SetName(console, "排障日志"); panel.Children.Add(console);
        long revision = -1;
        void Refresh()
        {
            if (!visible() || revision == DiagnosticLog.Revision) return;
            revision = DiagnosticLog.Revision;
            var snapshot = DiagnosticLog.Snapshot(); var offset = console.VerticalOffset;
            int selection = console.SelectionStart, length = console.SelectionLength;
            console.Text = string.Join(Environment.NewLine, snapshot.Select(x => x.ToString()));
            if (follow.IsChecked == true && length == 0) console.ScrollToEnd();
            else { console.Select(Math.Min(selection, console.Text.Length), Math.Min(length, Math.Max(0, console.Text.Length - selection))); console.ScrollToVerticalOffset(offset); }
            count.Text = $"{snapshot.Length} / {DiagnosticLog.Capacity} 条 · INFO / WARN · 自动刷新";
        }
        save.Click += (_, _) =>
        {
            var dialog = new SaveFileDialog { Title = "保存排障日志", DefaultExt = ".txt", Filter = "文本文件 (*.txt)|*.txt", FileName = $"BigBlueFish-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.txt" };
            if (dialog.ShowDialog(owner) != true) return;
            try { DiagnosticLog.SaveText(dialog.FileName); DiagnosticLog.Write(DiagnosticEvent.LogExported, directory: System.IO.Path.GetDirectoryName(dialog.FileName)); }
            catch (Exception error) { DiagnosticLog.Write(DiagnosticEvent.LogExportFailed, error: error); BigBlueFish.Deployment.ModernUI.Error(owner, "保存日志失败", error); }
        };
        var clock = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        clock.Tick += (_, _) => Refresh(); Refresh(); clock.Start(); return clock.Stop;
    }
}
