using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using Application = System.Windows.Application;
using Button = System.Windows.Controls.Button;
using Brushes = System.Windows.Media.Brushes;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace BigBlueFish.Deployment;

internal class ModernWindow : Window
{
    internal readonly StackPanel Body = new() { Margin = new Thickness(30) };
    internal readonly WrapPanel Actions = new() { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(30, 8, 22, 22) };
    internal ModernWindow(string title, string heading, string description, double width = 620, double height = 470)
    {
        Title = title; Width = Math.Min(width, SystemParameters.WorkArea.Width - 32);
        Height = Math.Min(height, SystemParameters.WorkArea.Height - 32);
        MinWidth = Math.Min(430, Width); MinHeight = Math.Min(300, Height);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(246, 247, 251));
        Foreground = new SolidColorBrush(Color.FromRgb(38, 51, 76));
        FontFamily = new FontFamily("Microsoft YaHei UI"); FontSize = 14;
        using var stream = typeof(ModernWindow).Assembly.GetManifestResourceStream("BigBlueFish.ModernTheme.xaml")!;
        Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Load(stream));
        Body.Children.Add(new TextBlock { Text = "蓝色大肥鱼", Foreground = ModernUI.Blue, FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) });
        Body.Children.Add(new TextBlock { Text = heading, FontSize = 26, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 12) });
        Body.Children.Add(ModernUI.Text(description));
        var shell = new DockPanel { Background = Background }; DockPanel.SetDock(Actions, Dock.Bottom); shell.Children.Add(Actions);
        shell.Children.Add(new ScrollViewer { Content = Body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        Content = shell;
    }
}

internal static class ModernUI
{
    internal static readonly Brush Blue = new SolidColorBrush(Color.FromRgb(80, 96, 152));
    internal static Application EnsureApplication() => Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
    internal static TextBlock Text(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, LineHeight = 24, Margin = new Thickness(0, 0, 0, 12) };
    internal static Button Button(string text, Action action, bool primary = false)
    {
        var button = new Button { Content = text };
        if (primary) { button.Background = Blue; button.Foreground = Brushes.White; button.BorderBrush = Blue; }
        button.Click += (_, _) => action(); return button;
    }
    internal static int Choice(Window? owner, string title, string message, params string[] labels)
    {
        EnsureApplication();
        var dialog = new ModernWindow(title, title, message, 570, 330) { ShowInTaskbar = owner == null };
        if (owner?.IsVisible == true) { dialog.Owner = owner; dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner; }
        int result = -1;
        for (int i = 0; i < labels.Length; i++)
        { int selected = i; dialog.Actions.Children.Add(Button(labels[i], () => { result = selected; dialog.Close(); }, i == 0)); }
        dialog.ShowDialog(); return result;
    }
    internal static void Error(Window? owner, string title, Exception error) => Choice(owner, title, error.Message, "确定");
}
