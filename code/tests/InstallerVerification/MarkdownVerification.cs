using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using BigBlueFish.Deployment;
using Button = System.Windows.Controls.Button;
using FontFamily = System.Windows.Media.FontFamily;
using TextBlock = System.Windows.Controls.TextBlock;

// Verifies the update page's Markdown rendering without needing a real published release:
// a fixture GitHub payload supplies the notes, then the real UpdateWindow is laid out.
internal static class UpdateNotesVerification
{
    private const string Notes = """
        ## 1.0.2 · 2026-11-01

        本次更新完善了**更新页面**，支持 *Markdown* 排版与 `行内代码`，并加入 ~~旧样式~~ 新体验。

        ### 新功能

        - 标题、列表与引用
        - **粗体** 与 *斜体*
        - 链接：[更新说明](https://example.com/notes)
        - [x] 已完成
        - [ ] 未完成

        > 引用：请不要在更新说明里写入隐私信息。

        ```text
        **这段代码不是粗体**
        # 也不是标题
        ```

        ---

        1. 第一步
        2. 第二步运行 `BigBlueFish.exe`

        完整变更：https://example.com/changelog
        """;

    private static int checks;

    internal static int Run(string root)
    {
        root = Path.GetFullPath(root);
        Directory.CreateDirectory(root);
        ModernUI.EnsureApplication();
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));

        var update = FixtureRelease().GetAwaiter().GetResult();
        VerifyRendering(update, root);
        VerifyFallbacks(root);
        VerifyLongNotes(root);

        Console.WriteLine($"RESULT {checks} update-page Markdown checks passed; release metadata and notes are offline fixtures");
        return 0;
    }

    private static async Task<AvailableUpdate> FixtureRelease()
    {
        string json = JsonSerializer.Serialize(new
        {
            draft = false,
            prerelease = false,
            tag_name = "v9.9.9",
            body = Notes,
            assets = new[]
            {
                new
                {
                    name = "BigBlueFish-v9.9.9-OCR-Setup.exe",
                    browser_download_url = "http://fixture/installer",
                    digest = "sha256:" + new string('a', 64),
                },
            },
        });
        using var client = new HttpClient(new Stub(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        }));
        var update = await UpdateService.Check("1.0.1", client: client);
        Check(update != null && update.Notes == Notes, "fixture release metadata carries the Markdown release notes");
        Check(await UpdateService.Check("9.9.9", client: client) == null, "no update page is offered when the installed version is already current");
        return update!;
    }

    private static void VerifyRendering(AvailableUpdate update, string root)
    {
        var window = new UpdateWindow(update, Path.Combine(root, "install"));
        Show(window);
        var blocks = Visual<TextBlock>(Content(window)).ToList();
        var inlines = blocks.SelectMany(block => AllInlines(block.Inlines)).ToList();

        Check(blocks.Any(block => Text(block) == "更新内容"), "update page labels the release notes section");
        Check(Text(Content(window)).Contains("可更新到 9.9.9"), "update page keeps the target version in its description");

        var heading = blocks.FirstOrDefault(block => Text(block) == "1.0.2 · 2026-11-01");
        Check(heading != null && heading.FontSize >= 18 && heading.FontWeight == FontWeights.Bold, "Markdown heading renders as a large bold title");

        var subheading = blocks.FirstOrDefault(block => Text(block) == "新功能");
        Check(subheading != null && subheading.FontSize >= 15 && subheading.FontSize < heading!.FontSize, "Markdown sub-heading is smaller than the title but larger than body text");

        Check(inlines.OfType<Bold>().Any(bold => Text(bold.Inlines).Contains("更新页面")), "double asterisks render a bold run");
        Check(inlines.OfType<Italic>().Any(italic => Text(italic.Inlines).Contains("Markdown")), "single asterisks render an italic run");
        Check(inlines.OfType<Run>().Any(run => run.Text == "行内代码" && IsMono(run.FontFamily)), "backticks render an inline code run");
        Check(inlines.OfType<Span>().Any(span => Text(span.Inlines).Contains("旧样式")
            && span.TextDecorations?.Any(decoration => decoration.Location == TextDecorationLocation.Strikethrough) == true), "double tildes render a struck-through run");
        Check(inlines.OfType<Hyperlink>().Any(link => link.NavigateUri?.AbsoluteUri == "https://example.com/notes" && Text(link.Inlines) == "更新说明"), "Markdown links render as a hyperlink with its target");
        Check(inlines.OfType<Hyperlink>().Any(link => link.NavigateUri?.AbsoluteUri == "https://example.com/changelog"), "bare HTTP(S) URLs render as hyperlinks like GitHub release notes");

        Check(blocks.Count(block => Text(block) == "•") >= 3, "unordered list items render with bullet markers");
        Check(blocks.Any(block => Text(block) == "☑") && blocks.Any(block => Text(block) == "☐"), "task list items render checked and unchecked markers");
        Check(blocks.Any(block => Text(block) == "1.") && blocks.Any(block => Text(block) == "2."), "ordered list items keep their numbers");

        Check(Visual<Border>(Content(window)).Any(border => border.BorderThickness.Left == 3
            && Text(border).Contains("引用：请不要在更新说明里写入隐私信息。")), "block quotes render with an accent bar");
        Check(Visual<Border>(Content(window)).Any(border => border.Height == 1), "horizontal rules render as a divider");
        Check(blocks.Any(block => IsMono(block.FontFamily) && Text(block).Contains("**这段代码不是粗体**")
            && Text(block).Contains("# 也不是标题")), "fenced code preserves Markdown-looking lines literally in a monospace block");

        var visible = blocks.Where(block => !IsMono(block.FontFamily)).Select(block => Text(block)).ToList();
        Check(visible.All(text => !text.Contains("**") && !text.Contains("## ") && !text.Contains("](") && !text.Contains('`')),
            "raw Markdown markers do not leak into the rendered text");

        var actions = ((ModernWindow)window).Actions.Children.OfType<Button>().ToList();
        Check(actions.Select(button => (string)button.Content).SequenceEqual(new[] { "稍后", "下载并更新" }), "update page keeps both download actions");

        Render(window, root, "update-notes");
        CheckActions(window, actions);
        window.Close();
        Pump();
    }

    private static void VerifyFallbacks(string root)
    {
        var empty = new UpdateWindow(new AvailableUpdate("9.9.9", "BigBlueFish.exe", "http://fixture/installer", new string('a', 64), "  \n "), Path.Combine(root, "install"));
        Show(empty);
        Check(Visual<TextBlock>(Content(empty)).Any(block => Text(block) == "本次更新没有提供详细说明。"), "empty release notes show a placeholder instead of an empty page");
        empty.Close(); Pump();

        var plain = new UpdateWindow(new AvailableUpdate("9.9.9", "BigBlueFish.exe", "http://fixture/installer", new string('a', 64), "修复了若干问题。\n感谢反馈。"), Path.Combine(root, "install"));
        Show(plain);
        Check(Text(Content(plain)).Contains("修复了若干问题。 感谢反馈。"), "plain text release notes still render as a paragraph");
        plain.Close(); Pump();

        var unsafeWindow = new UpdateWindow(new AvailableUpdate("9.9.9", "BigBlueFish.exe", "http://fixture/installer", new string('a', 64),
            "[点我](javascript:alert(1)) 与 <b>不是粗体</b>"), Path.Combine(root, "install"));
        Show(unsafeWindow);
        var unsafeInlines = Visual<TextBlock>(Content(unsafeWindow)).SelectMany(block => AllInlines(block.Inlines)).ToList();
        Check(!unsafeInlines.OfType<Hyperlink>().Any(), "non HTTP(S) link targets never become clickable hyperlinks");
        Check(Text(Content(unsafeWindow)).Contains("[点我](javascript:alert(1))") && Text(Content(unsafeWindow)).Contains("<b>不是粗体</b>"),
            "unsafe link schemes and HTML-like text are shown literally");
        unsafeWindow.Close(); Pump();
    }

    private static void VerifyLongNotes(string root)
    {
        string notes = string.Join("\n", Enumerable.Range(1, 80).Select(index => $"- 第 {index} 条更新说明，用于验证长内容可以在窗口内滚动查看。"));
        var window = new UpdateWindow(new AvailableUpdate("9.9.9", "BigBlueFish.exe", "http://fixture/installer", new string('a', 64), notes), Path.Combine(root, "install"));
        Show(window);
        var scroll = Visual<ScrollViewer>(Content(window)).FirstOrDefault();
        Check(scroll != null && scroll.ScrollableHeight > 0, "long release notes scroll inside the update page");
        var actions = ((ModernWindow)window).Actions.Children.OfType<Button>().ToList();
        CheckActions(window, actions);
        window.Close(); Pump();
    }

    private static void CheckActions(Window window, IReadOnlyList<Button> actions)
    {
        foreach (var button in actions)
        {
            var rect = button.TransformToAncestor(window).TransformBounds(new Rect(0, 0, button.ActualWidth, button.ActualHeight));
            Check(rect.Bottom <= window.ActualHeight && rect.Top >= 0, "update action stays visible: " + button.Content);
        }
    }

    private static void Render(Window window, string root, string name)
    {
        foreach (double dpi in new[] { 96d, 144d, 216d })
        {
            var bitmap = new RenderTargetBitmap((int)(window.ActualWidth * dpi / 96), (int)(window.ActualHeight * dpi / 96), dpi, dpi, PixelFormats.Pbgra32);
            bitmap.Render((Visual)window.Content);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(root, $"{name}-{dpi:0}.png"));
            encoder.Save(file);
        }
    }

    private static void Show(Window window)
    {
        window.Show();
        Pump();
        window.UpdateLayout();
    }

    private static DependencyObject Content(Window window) => (DependencyObject)window.Content;

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static IEnumerable<T> Visual<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T item) yield return item;
            foreach (var nested in Visual<T>(child)) yield return nested;
        }
    }

    private static IEnumerable<Inline> AllInlines(InlineCollection collection)
    {
        foreach (var inline in collection)
        {
            yield return inline;
            if (inline is Span span)
                foreach (var nested in AllInlines(span.Inlines)) yield return nested;
        }
    }

    private static string Text(TextBlock block) => block.Inlines.Count > 0 ? Text(block.Inlines) : block.Text;

    private static string Text(InlineCollection collection) => string.Concat(collection.Select(inline => inline switch
    {
        Run run => run.Text,
        Span span => Text(span.Inlines),
        LineBreak => "\n",
        _ => string.Empty,
    }));

    private static string Text(DependencyObject root) => string.Join("\n", Visual<TextBlock>(root).Select(block => Text(block)));

    private static bool IsMono(FontFamily family) => family.Source.Contains("Consolas", StringComparison.OrdinalIgnoreCase);

    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
        checks++;
        Console.WriteLine("PASS " + message);
    }

    private sealed class Stub(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(respond(request));
    }
}
