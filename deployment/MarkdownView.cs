using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Navigation;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Cursors = System.Windows.Input.Cursors;
using FontFamily = System.Windows.Media.FontFamily;
using Panel = System.Windows.Controls.Panel;

namespace BigBlueFish.Deployment;

// Renders the release notes shown on the update page. This is intentionally a small,
// dependency-free subset of GitHub-flavoured Markdown: headings, paragraphs, emphasis,
// inline/fenced code, links, quotes, lists and horizontal rules.
internal static class MarkdownView
{
    private static readonly Brush Ink = Frozen(Color.FromRgb(38, 51, 76));
    private static readonly Brush Muted = Frozen(Color.FromRgb(103, 115, 138));
    private static readonly Brush CodeInk = Frozen(Color.FromRgb(54, 66, 92));
    private static readonly Brush CodeBackground = Frozen(Color.FromRgb(236, 239, 247));
    private static readonly Brush QuoteBackground = Frozen(Color.FromRgb(243, 245, 251));
    private static readonly Brush Rule = Frozen(Color.FromRgb(223, 227, 237));
    private static readonly FontFamily Mono = new("Consolas, Cascadia Mono, Courier New");
    private static readonly Regex HeadingPattern = new(@"^\s{0,3}(#{1,6})\s+(.*?)\s*$", RegexOptions.Compiled);
    private static readonly Regex RulePattern = new(@"^([-*_])(?:\s*\1){2,}$", RegexOptions.Compiled);
    private static readonly Regex ListPattern = new(@"^(\s*)([-*+]|\d{1,9}[.)])\s+(.*)$", RegexOptions.Compiled);
    private static readonly Regex TrailingHashes = new(@"\s+#+\s*$", RegexOptions.Compiled);
    private static readonly char[] LinkSeparators = { ' ', '\t' };

    internal static FrameworkElement Render(string? markdown)
    {
        var panel = new StackPanel();
        var lines = (markdown ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        RenderBlocks(lines, panel);
        return panel;
    }

    private static void RenderBlocks(IReadOnlyList<string> lines, Panel panel)
    {
        for (int i = 0; i < lines.Count;)
        {
            string line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) { i++; continue; }
            string trimmed = line.TrimStart();

            if (IsFence(trimmed, out string fence)) { i = AppendCode(lines, i, fence, panel); continue; }
            if (TryHeading(line, out int level, out string heading)) { AppendHeading(panel, level, heading); i++; continue; }
            if (RulePattern.IsMatch(trimmed)) { panel.Children.Add(new Border { Height = 1, Background = Rule, Margin = new Thickness(0, 2, 0, 14) }); i++; continue; }
            if (trimmed.StartsWith(">", StringComparison.Ordinal)) { i = AppendQuote(lines, i, panel); continue; }
            if (ListPattern.IsMatch(line)) { i = AppendList(lines, i, panel); continue; }
            i = AppendParagraph(lines, i, panel);
        }
    }

    private static bool StartsBlock(string line)
    {
        string trimmed = line.TrimStart();
        return IsFence(trimmed, out _) || TryHeading(line, out _, out _) || RulePattern.IsMatch(trimmed)
            || trimmed.StartsWith(">", StringComparison.Ordinal) || ListPattern.IsMatch(line);
    }

    private static int AppendParagraph(IReadOnlyList<string> lines, int start, Panel panel)
    {
        var text = new StringBuilder();
        int i = start;
        while (i < lines.Count && !string.IsNullOrWhiteSpace(lines[i]))
        {
            if (i > start && StartsBlock(lines[i])) break;
            if (text.Length > 0) text.Append(lines[i - 1].EndsWith("  ", StringComparison.Ordinal) ? "\n" : " ");
            text.Append(lines[i].Trim());
            i++;
        }
        var block = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 23,
            Foreground = Ink,
            Margin = new Thickness(0, 0, 0, 12),
        };
        ParseInlines(text.ToString(), block.Inlines);
        panel.Children.Add(block);
        return i;
    }

    private static bool TryHeading(string line, out int level, out string text)
    {
        var match = HeadingPattern.Match(line);
        if (!match.Success) { level = 0; text = string.Empty; return false; }
        level = match.Groups[1].Value.Length;
        text = TrailingHashes.Replace(match.Groups[2].Value, string.Empty).Trim();
        return true;
    }

    private static void AppendHeading(Panel panel, int level, string text)
    {
        double[] sizes = { 0, 23, 19, 16.5, 15, 14, 13.5 };
        double size = sizes[Math.Clamp(level, 1, 6)];
        var block = new TextBlock
        {
            FontSize = size,
            FontWeight = FontWeights.Bold,
            Foreground = Ink,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = Math.Round(size * 1.35),
            Margin = new Thickness(0, panel.Children.Count == 0 ? 0 : 16, 0, 8),
        };
        ParseInlines(text, block.Inlines);
        panel.Children.Add(block);
    }

    private static bool IsFence(string trimmed, out string fence)
    {
        fence = string.Empty;
        if (trimmed.Length == 0) return false;
        char marker = trimmed[0];
        if (marker != '`' && marker != '~') return false;
        int count = 0;
        while (count < trimmed.Length && trimmed[count] == marker) count++;
        if (count < 3) return false;
        fence = new string(marker, count);
        return true;
    }

    private static int AppendCode(IReadOnlyList<string> lines, int start, string fence, Panel panel)
    {
        int i = start + 1;
        var code = new List<string>();
        while (i < lines.Count && !lines[i].TrimStart().StartsWith(fence, StringComparison.Ordinal))
        {
            code.Add(lines[i]);
            i++;
        }
        if (i < lines.Count) i++;
        panel.Children.Add(new Border
        {
            Background = CodeBackground,
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(12, 9, 12, 9),
            Margin = new Thickness(0, 2, 0, 14),
            Child = new TextBlock
            {
                Text = string.Join("\n", code),
                FontFamily = Mono,
                FontSize = 12.5,
                Foreground = CodeInk,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 19,
            },
        });
        return i;
    }

    private static int AppendQuote(IReadOnlyList<string> lines, int start, Panel panel)
    {
        var nested = new List<string>();
        int i = start;
        while (i < lines.Count)
        {
            string line = lines[i].TrimStart();
            if (!line.StartsWith(">", StringComparison.Ordinal)) break;
            string content = line[1..];
            if (content.StartsWith(" ", StringComparison.Ordinal)) content = content[1..];
            nested.Add(content);
            i++;
        }
        var body = new StackPanel();
        RenderBlocks(nested, body);
        panel.Children.Add(new Border
        {
            Background = QuoteBackground,
            BorderBrush = ModernUI.Blue,
            BorderThickness = new Thickness(3, 0, 0, 0),
            CornerRadius = new CornerRadius(0, 6, 6, 0),
            Padding = new Thickness(14, 8, 12, 0),
            Margin = new Thickness(0, 0, 0, 14),
            Child = body,
        });
        return i;
    }

    private static int AppendList(IReadOnlyList<string> lines, int start, Panel panel)
    {
        int i = start;
        while (i < lines.Count)
        {
            var match = ListPattern.Match(lines[i]);
            if (!match.Success) break;
            int indent = match.Groups[1].Value.Replace("\t", "    ").Length;
            string marker = match.Groups[2].Value;
            var text = new StringBuilder(match.Groups[3].Value.Trim());
            i++;
            while (i < lines.Count && !string.IsNullOrWhiteSpace(lines[i]) && !ListPattern.IsMatch(lines[i])
                   && (lines[i].StartsWith("  ", StringComparison.Ordinal) || lines[i].StartsWith('\t')))
            {
                text.Append(' ').Append(lines[i].Trim());
                i++;
            }
            AppendListItem(panel, indent / 2, marker, text.ToString());
        }
        return i;
    }

    private static void AppendListItem(Panel panel, int level, string marker, string text)
    {
        level = Math.Clamp(level, 0, 3);
        if (text.StartsWith("[ ] ", StringComparison.Ordinal)) { marker = "☐"; text = text[4..]; }
        else if (text.StartsWith("[x] ", StringComparison.OrdinalIgnoreCase)) { marker = "☑"; text = text[4..]; }
        bool ordered = char.IsDigit(marker[0]);
        if (!ordered && marker != "☑" && marker != "☐") marker = "•";

        var row = new Grid { Margin = new Thickness(level * 18, 0, 0, 7) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var bullet = new TextBlock
        {
            Text = marker,
            Foreground = ordered ? ModernUI.Blue : Muted,
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
            LineHeight = 22,
            VerticalAlignment = VerticalAlignment.Top,
        };
        var body = new TextBlock { TextWrapping = TextWrapping.Wrap, LineHeight = 22, Foreground = Ink };
        ParseInlines(text, body.Inlines);
        Grid.SetColumn(bullet, 0); Grid.SetColumn(body, 1);
        row.Children.Add(bullet); row.Children.Add(body);
        panel.Children.Add(row);
    }

    private static void ParseInlines(string text, InlineCollection target)
    {
        var pending = new StringBuilder();
        int i = 0;

        while (i < text.Length)
        {
            char c = text[i];

            if (c == '\\' && i + 1 < text.Length && IsEscapable(text[i + 1]))
            {
                pending.Append(text[i + 1]); i += 2; continue;
            }
            if (c == '\n') { Flush(pending, target); target.Add(new LineBreak()); i++; continue; }
            if (c == '`' && TryCodeSpan(text, i, pending, target, ref i)) continue;
            if (c == '~' && i + 1 < text.Length && text[i + 1] == '~' && TryStrike(text, i, pending, target, ref i)) continue;
            if (c == '[' && TryLink(text, i, pending, target, ref i)) continue;
            if (c == '<' && TryAutoLink(text, i, pending, target, ref i)) continue;
            if ((c == 'h' || c == 'H') && TryBareLink(text, i, pending, target, ref i)) continue;
            if ((c == '*' || c == '_') && !(c == '_' && i > 0 && char.IsLetterOrDigit(text[i - 1])))
            {
                if (TryEmphasis(text, i, pending, target, ref i)) continue;
            }

            pending.Append(c);
            i++;
        }
        Flush(pending, target);
    }

    private static bool TryCodeSpan(string text, int start, StringBuilder pending, InlineCollection target, ref int next)
    {
        int count = RunLength(text, start, '`');
        string fence = new string('`', count);
        int close = text.IndexOf(fence, start + count, StringComparison.Ordinal);
        if (close < 0) return false;
        Flush(pending, target);
        string code = text.Substring(start + count, close - start - count).Replace('\n', ' ');
        target.Add(new Run(code)
        {
            FontFamily = Mono,
            FontSize = 12.5,
            Foreground = CodeInk,
            Background = CodeBackground,
        });
        next = close + count;
        return true;
    }

    private static bool TryStrike(string text, int start, StringBuilder pending, InlineCollection target, ref int next)
    {
        int close = text.IndexOf("~~", start + 2, StringComparison.Ordinal);
        if (close < 0) return false;
        Flush(pending, target);
        var strike = new Span { TextDecorations = TextDecorations.Strikethrough };
        ParseInlines(text.Substring(start + 2, close - start - 2), strike.Inlines);
        target.Add(strike);
        next = close + 2;
        return true;
    }

    private static bool TryEmphasis(string text, int start, StringBuilder pending, InlineCollection target, ref int next)
    {
        char marker = text[start];
        int run = RunLength(text, start, marker);
        int width = run >= 3 ? 3 : run == 2 ? 2 : 1;
        int close = FindClosing(text, start + width, marker, width);
        if (close < 0) return false;

        Flush(pending, target);
        string content = text.Substring(start + width, close - start - width);
        if (width == 3)
        {
            var bold = new Bold();
            var italic = new Italic();
            ParseInlines(content, italic.Inlines);
            bold.Inlines.Add(italic);
            target.Add(bold);
        }
        else if (width == 2)
        {
            var bold = new Bold();
            ParseInlines(content, bold.Inlines);
            target.Add(bold);
        }
        else
        {
            var italic = new Italic();
            ParseInlines(content, italic.Inlines);
            target.Add(italic);
        }
        next = close + width;
        return true;
    }

    private static bool TryLink(string text, int start, StringBuilder pending, InlineCollection target, ref int next)
    {
        int labelEnd = text.IndexOf("](", start + 1, StringComparison.Ordinal);
        if (labelEnd < 0) return false;
        int urlEnd = text.IndexOf(')', labelEnd + 2);
        if (urlEnd < 0) return false;
        string label = text.Substring(start + 1, labelEnd - start - 1);
        string url = text.Substring(labelEnd + 2, urlEnd - labelEnd - 2).Trim();
        if (url.StartsWith("<", StringComparison.Ordinal) && url.EndsWith(">", StringComparison.Ordinal)) url = url[1..^1];
        int separator = url.IndexOfAny(LinkSeparators);
        if (separator > 0) url = url[..separator];
        if (!TryCreateUri(url, out var uri)) return false;

        Flush(pending, target);
        var link = new Hyperlink
        {
            NavigateUri = uri,
            Foreground = ModernUI.Blue,
            Cursor = Cursors.Hand,
            ToolTip = uri.AbsoluteUri,
        };
        link.RequestNavigate += OnNavigate;
        ParseInlines(label, link.Inlines);
        target.Add(link);
        next = urlEnd + 1;
        return true;
    }

    private static bool TryAutoLink(string text, int start, StringBuilder pending, InlineCollection target, ref int next)
    {
        int close = text.IndexOf('>', start + 1);
        if (close < 0) return false;
        string value = text.Substring(start + 1, close - start - 1);
        if (!TryCreateUri(value, out var uri)) return false;

        Flush(pending, target);
        var link = new Hyperlink
        {
            NavigateUri = uri,
            Foreground = ModernUI.Blue,
            Cursor = Cursors.Hand,
            ToolTip = uri.AbsoluteUri,
        };
        link.Inlines.Add(new Run(value));
        link.RequestNavigate += OnNavigate;
        target.Add(link);
        next = close + 1;
        return true;
    }

    private static bool TryBareLink(string text, int start, StringBuilder pending, InlineCollection target, ref int next)
    {
        if (start > 0 && (char.IsLetterOrDigit(text[start - 1]) || text[start - 1] == '_')) return false;
        int end = start;
        while (end < text.Length && !char.IsWhiteSpace(text[end]) && text[end] is not ('<' or '>' or ')' or ']' or '"')) end++;
        string value = text[start..end].TrimEnd('.', ',', ';', ':', '!', '?', '。', '，', '；', '：', '！', '？');
        if (!value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !value.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return false;
        if (!TryCreateUri(value, out var uri)) return false;

        Flush(pending, target);
        var link = new Hyperlink
        {
            NavigateUri = uri,
            Foreground = ModernUI.Blue,
            Cursor = Cursors.Hand,
            ToolTip = uri.AbsoluteUri,
        };
        link.Inlines.Add(new Run(value));
        link.RequestNavigate += OnNavigate;
        target.Add(link);
        next = start + value.Length;
        return true;
    }

    private static int FindClosing(string text, int start, char marker, int width)
    {
        for (int i = start; i <= text.Length - width; i++)
        {
            if (text[i] != marker) continue;
            if (i > 0 && text[i - 1] == '\\') continue;
            int run = RunLength(text, i, marker);
            if (run == width) return i;
            i += run - 1;
        }
        return -1;
    }

    private static int RunLength(string text, int start, char marker)
    {
        int i = start;
        while (i < text.Length && text[i] == marker) i++;
        return i - start;
    }

    private static void Flush(StringBuilder pending, InlineCollection target)
    {
        if (pending.Length == 0) return;
        target.Add(new Run(pending.ToString()));
        pending.Clear();
    }

    private static bool TryCreateUri(string value, out Uri uri)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var parsed) && parsed != null
            && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps || parsed.Scheme == Uri.UriSchemeMailto))
        {
            uri = parsed;
            return true;
        }
        uri = null!;
        return false;
    }

    private static void OnNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            if (e.Uri.Scheme == Uri.UriSchemeHttp || e.Uri.Scheme == Uri.UriSchemeHttps || e.Uri.Scheme == Uri.UriSchemeMailto)
                Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception) { }
        e.Handled = true;
    }

    private static bool IsEscapable(char value) => "\\`*_{}[]()#+-.!>~|".Contains(value);

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
