using System.Text;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace BigBlueFish.Windows;
internal sealed partial class LinuxPetWindow
{
    Action BuildLogConsole(StackPanel panel)
    {
        panel.Children.Add(Paragraph($"本次运行的排障日志，保留最近{DiagnosticLog.Capacity}条。记录操作、状态、耗时和错误码；不包含截图、OCR、聊天或提示词正文，目录已匿名化。"));
        var follow = new CheckBox { Content = "自动滚动", IsChecked = true, VerticalAlignment = VerticalAlignment.Center };
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        toolbar.Children.Add(ActionButton("保存为 TXT…", async () => {
            if (settings == null) return;
            try
            {
                var file = await settings.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "保存排障日志", SuggestedFileName = $"BigBlueFish-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.txt", DefaultExtension = "txt", FileTypeChoices = [new FilePickerFileType("文本文件") { Patterns = ["*.txt"] }] });
                if (file == null) return;
                await using var stream = await file.OpenWriteAsync(); if (stream.CanSeek) stream.SetLength(0);
                await using var writer = new StreamWriter(stream, new UTF8Encoding(true)); await writer.WriteAsync(DiagnosticLog.ExportText()); await writer.FlushAsync();
                DiagnosticLog.Write(DiagnosticEvent.LogExported);
            }
            catch (Exception error) { DiagnosticLog.Write(DiagnosticEvent.LogExportFailed, error: error); }
        }));
        toolbar.Children.Add(follow); panel.Children.Add(toolbar);
        var count = Paragraph(""); panel.Children.Add(count);
        var console = new TextBox { Name = "LogConsoleText", IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, FontFamily = new FontFamily("DejaVu Sans Mono, Noto Sans CJK SC"), FontSize = 12, Height = 400 };
        ScrollViewer.SetVerticalScrollBarVisibility(console, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
        ScrollViewer.SetHorizontalScrollBarVisibility(console, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto); panel.Children.Add(console);
        long revision = -1;
        void Refresh()
        {
            if (revision == DiagnosticLog.Revision) return;
            revision = DiagnosticLog.Revision; var snapshot = DiagnosticLog.Snapshot();
            console.Text = string.Join(Environment.NewLine, snapshot.Select(x => x.ToString()));
            if (follow.IsChecked == true) console.CaretIndex = console.Text.Length;
            count.Text = $"{snapshot.Length} / {DiagnosticLog.Capacity} 条 · INFO / WARN · 自动刷新";
        }
        var clock = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) }; clock.Tick += (_, _) => Refresh(); Refresh(); clock.Start(); return clock.Stop;
    }
}
