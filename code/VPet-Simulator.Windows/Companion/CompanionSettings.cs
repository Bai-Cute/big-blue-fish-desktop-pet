using Microsoft.Win32;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace VPet_Simulator.Windows;
public partial class MainWindow
{
    private void OpenCompanionSettings()
    {
        if (exitAnimation) return;
        if (companionSettings != null)
        {
            companionSettings.Activate();
            return;
        }

        CancelMovement();
        var w = new Window
        {
            Title = "大肥鱼设置",
            Width = Math.Min(860, SystemParameters.WorkArea.Width - 32),
            Height = Math.Min(690, SystemParameters.WorkArea.Height - 32),
            MinWidth = Math.Min(680, SystemParameters.WorkArea.Width - 32),
            MinHeight = Math.Min(450, SystemParameters.WorkArea.Height - 32),
            ResizeMode = ResizeMode.CanResize,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = new SolidColorBrush(Color.FromRgb(246, 248, 252)),
            FontFamily = new FontFamily("Microsoft YaHei UI"),
            FontSize = 14,
            ShowInTaskbar = true
        };
        w.Resources.MergedDictionaries.Add(new ResourceDictionary
        { Source = new Uri("/VPet-Simulator.Windows;component/Companion/CompanionSettingsStyles.xaml", UriKind.Relative) });
        var shell = new Grid { Background = w.Background };
        shell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
        shell.ColumnDefinitions.Add(new ColumnDefinition());
        w.Content = shell;
        var sidebar = new StackPanel { Margin = new Thickness(18, 24, 18, 18) };
        var sidebarFrame = new Border { Background = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(226, 233, 243)), BorderThickness = new Thickness(0, 0, 1, 0), Child = sidebar };
        shell.Children.Add(sidebarFrame);
        sidebar.Children.Add(new TextBlock { Text = "蓝色大肥鱼", FontSize = 22, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(53, 76, 129)), Margin = new Thickness(5, 0, 0, 8) });
        var version = typeof(MainWindow).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .Cast<System.Reflection.AssemblyInformationalVersionAttribute>().First().InformationalVersion.Split('+')[0];
        sidebar.Children.Add(new TextBlock { Text = version + " · 陪伴设置", Foreground = Brushes.SlateGray, Margin = new Thickness(5, 0, 0, 28) });
        var pages = new StackPanel[3];
        var views = new ScrollViewer[3];
        var navigation = new Button[3];
        var titles = new[] { "陪伴与外观", "说话与天气", "模型与供电" };
        for (int i = 0; i < pages.Length; i++)
        {
            pages[i] = new StackPanel { Margin = new Thickness(26, 24, 26, 24) };
            pages[i].Children.Add(new TextBlock { Text = titles[i], FontSize = 25, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(42, 56, 83)), Margin = new Thickness(0, 0, 0, 20) });
            views[i] = new ScrollViewer { Content = pages[i], VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Visibility = i == 0 ? Visibility.Visible : Visibility.Collapsed };
            Grid.SetColumn(views[i], 1); shell.Children.Add(views[i]);
            int selected = i;
            var button = navigation[i] = new Button { Content = titles[i], HorizontalContentAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 10), BorderThickness = new Thickness(0), Background = i == 0 ? new SolidColorBrush(Color.FromRgb(220, 234, 255)) : Brushes.Transparent };
            button.Click += (_, _) =>
            {
                for (int n = 0; n < views.Length; n++)
                { views[n].Visibility = n == selected ? Visibility.Visible : Visibility.Collapsed; navigation[n].Background = n == selected ? new SolidColorBrush(Color.FromRgb(220, 234, 255)) : Brushes.Transparent; }
            };
            sidebar.Children.Add(button);
        }
        void Card(StackPanel panel)
        {
            var children = panel.Children.Cast<UIElement>().Skip(1).ToArray();
            foreach (var child in children) panel.Children.Remove(child);
            var inner = new StackPanel { Margin = new Thickness(20) };
            foreach (var child in children) inner.Children.Add(child);
            panel.Children.Add(new Border { Background = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(226, 233, 243)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Child = inner });
        }
        var p = pages[2];
        void Toggle(string label, bool value, Action<bool> change)
        {
            var c = new CheckBox
            {
                Content = label,
                IsChecked = value,
                Margin = new Thickness(0, 7, 0, 7)
            };
            c.Click += (_, _) =>
            {
                change(c.IsChecked == true);
                SaveCompanion();
            };
            p.Children.Add(c);
        }

        Toggle("启用本地大模型", preferences.ModelEnabled, v =>
        {
            preferences.ModelEnabled = v;
            if (!v)
                brain.Stop();
            else
            {
                nextWarmup = DateTime.MinValue;
                nextSpeech = DateTime.UtcNow.AddSeconds(20);
            }
        });
        Toggle("拔电时暂停并卸载模型", preferences.StopModelOnBattery, v =>
        {
            preferences.StopModelOnBattery = v;
            ApplyCompanionPowerPolicy();
        });
        Toggle("使用显卡加速（关闭则使用 CPU）", preferences.GpuEnabled, v =>
        {
            preferences.GpuEnabled = v;
            brain.Stop();
            brain.UseGpu = v;
            nextWarmup = DateTime.MinValue;
        });
        p = pages[0];
        Toggle("开机自动启动", StartupEnabled(), SetCompanionStartup);
        p = pages[1];
        Toggle("自动获取天气和科技新闻", preferences.PublicInfo, v => preferences.PublicInfo = v);
        Toggle("感知当前应用（仅在本机处理）", preferences.ForegroundEnabled, v => preferences.ForegroundEnabled = v);
        AddCompanionRegionSelectors(p);
        p = pages[0];
        var label = new TextBlock
        {
            Text = $"缩放：{preferences.Scale:P0}",
            Margin = new Thickness(0, 10, 0, 4)
        };
        p.Children.Add(label);
        var scale = new Slider
        {
            Minimum = .65,
            Maximum = 1.6,
            Value = preferences.Scale,
            TickFrequency = .05,
            IsSnapToTickEnabled = true
        };
        scale.ValueChanged += (_, _) =>
        {
            preferences.Scale = scale.Value;
            label.Text = $"缩放：{scale.Value:P0}";
            ApplyCompanionScale();
        };
        p.Children.Add(scale);
        var intervalLabel = new TextBlock { Text = $"动作触发间隔：{preferences.AnimationIntervalSeconds} 秒", Margin = new Thickness(0, 18, 0, 4) };
        p.Children.Add(intervalLabel);
        var interval = new Slider { Minimum = 30, Maximum = 300, Value = preferences.AnimationIntervalSeconds, TickFrequency = 10, IsSnapToTickEnabled = true };
        System.Windows.Automation.AutomationProperties.SetName(interval, "动作触发间隔");
        interval.ValueChanged += (_, _) =>
        {
            preferences.AnimationIntervalSeconds = (int)Math.Round(interval.Value);
            intervalLabel.Text = $"动作触发间隔：{preferences.AnimationIntervalSeconds} 秒";
            nextAction = DateTime.UtcNow.AddSeconds(preferences.AnimationIntervalSeconds);
            SaveCompanion();
        };
        p.Children.Add(interval);
        var meals = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 12) };
        void MealTime(string labelText, int initialHour, Action<int> change)
        {
            var column = new StackPanel { Margin = new Thickness(0, 0, 14, 0), Width = 110 };
            column.Children.Add(new TextBlock { Text = labelText, Margin = new Thickness(0, 0, 0, 6) });
            var selector = new ComboBox();
            for (int hour = 0; hour < 24; hour++) selector.Items.Add($"{hour:00}:00");
            selector.SelectedIndex = initialHour;
            selector.SelectionChanged += (_, _) => { if (selector.SelectedIndex >= 0) { change(selector.SelectedIndex); SaveCompanion(); } };
            column.Children.Add(selector); meals.Children.Add(column);
        }
        MealTime("早餐时间", preferences.BreakfastHour, h => preferences.BreakfastHour = h);
        MealTime("午餐时间", preferences.LunchHour, h => preferences.LunchHour = h);
        MealTime("晚餐时间", preferences.DinnerHour, h => preferences.DinnerHour = h);
        p.Children.Add(meals);
        p = pages[1];
        var freq = new ComboBox
        {
            Margin = new Thickness(0, 8, 0, 8)
        };
        foreach (var n in new[]
        {
            3,
            8,
            15,
            30
        }

        )
            freq.Items.Add($"说话间隔：{n}–{n + 7} 分钟");
        freq.SelectedIndex = preferences.SpeechMinMinutes switch
        {
            3 => 0,
            15 => 2,
            30 => 3,
            _ => 1
        };
        freq.SelectionChanged += (_, _) =>
        {
            preferences.SpeechMinMinutes = new[]
            {
                3,
                8,
                15,
                30
            }[freq.SelectedIndex];
            nextSpeech = DateTime.UtcNow.AddMinutes(preferences.SpeechMinMinutes);
            SaveCompanion();
        };
        p.Children.Add(freq);
        var dwellLabel = new TextBlock
        {
            Text = "气泡驻留时间：" + CompanionPreferences.BubbleDurationLabels[preferences.BubbleDurationIndex],
            Margin = new Thickness(0, 8, 0, 4)
        };
        p.Children.Add(dwellLabel);
        var dwell = new Slider
        {
            Minimum = 0,
            Maximum = 7,
            Value = preferences.BubbleDurationIndex,
            TickFrequency = 1,
            IsSnapToTickEnabled = true,
            SmallChange = 1,
            LargeChange = 1,
            TickPlacement = System.Windows.Controls.Primitives.TickPlacement.BottomRight,
            ToolTip = dwellLabel.Text
        };
        System.Windows.Automation.AutomationProperties.SetName(dwell, "气泡驻留时间");
        dwell.ValueChanged += (_, _) =>
        {
            preferences.BubbleDurationIndex = (int)Math.Round(dwell.Value);
            dwellLabel.Text = "气泡驻留时间：" + CompanionPreferences.BubbleDurationLabels[preferences.BubbleDurationIndex];
            dwell.ToolTip = dwellLabel.Text;
            if (bubble.Visibility == Visibility.Visible)
                ResetBubbleDeadline();
            SaveCompanion();
        };
        p.Children.Add(dwell);
        p = pages[2];
        var status = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.SlateGray,
            Margin = new Thickness(0, 8, 0, 8)
        };
        p.Children.Add(status);
        var timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        void UpdateStatus() => status.Text = (CompanionNative.OnAC ? "已插电" :
            preferences.StopModelOnBattery ? "电池供电 · 已按设置暂停模型" : "电池供电 · 允许模型运行") + "\n" + brain.Status;
        timer.Tick += (_, _) => UpdateStatus();
        UpdateStatus();
        timer.Start();
        void Button(string text, Action action)
        {
            var b = new Button
            {
                Content = text,
                Padding = new Thickness(8),
                Margin = new Thickness(0, 4, 0, 4)
            };
            b.Click += (_, _) => action();
            p.Children.Add(b);
        }

        p = pages[0];
        Button("隐藏到任务栏通知区", HideCompanion);
        Button("回到右下角", () =>
        {
            var a = SystemParameters.WorkArea;
            Left = a.Right - Width - 8;
            Top = a.Bottom - Height + 12;
            ClampCompanion();
            anchor = new Point(Left, Top);
            SaveCompanion();
        });
        Button("关闭桌宠", () =>
        {
            w.Close();
            base.Close();
        });
        foreach (var panel in pages) Card(panel);
        companionSettings = w;
        w.Closed += (_, _) =>
        {
            timer.Stop();
            companionSettings = null;
            SaveCompanion();
        };
        w.Show();
    }

}
