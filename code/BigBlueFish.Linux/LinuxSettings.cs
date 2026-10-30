using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Threading;
#if !COMPANION_SAFE
using BigBlueFish.Control;
#endif

namespace BigBlueFish.Windows;

internal sealed partial class LinuxPetWindow
{
    bool consentOpen;
    Window? consentWindow;
    Action? refreshLinuxSettings;
    static string AutostartFile => Path.Combine(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"), "autostart/bigbluefish.desktop");
    bool StartupEnabled => File.Exists(AutostartFile);
    void SetStartup(bool on)
    {
        if (!on) { File.Delete(AutostartFile); return; }
        Directory.CreateDirectory(Path.GetDirectoryName(AutostartFile)!);
        File.WriteAllText(AutostartFile, "[Desktop Entry]\nType=Application\nName=蓝色大肥鱼\nExec=\"" + Path.Combine(AppContext.BaseDirectory, "BigBlueFish.Linux").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"\nTerminal=false\n");
    }
    void ApplySettings(PixelPoint? requestedPosition = null)
    {
        speechVersion++; brain.Stop(); brain.UseGpu = Preferences.GpuEnabled; brain.StopOnBattery = Preferences.StopModelOnBattery; brain.WeatherRegionCode = Preferences.WeatherRegionCode;
        ConfigureAgent();
        Rescale(); var appliedPosition = Clamp(requestedPosition); nextSpeech = DateTime.UtcNow.Add(Preferences.NextSpeechDelay(random)); nextEvent = DateTime.UtcNow.AddSeconds(Preferences.AnimationIntervalSeconds); nextWarmup = DateTime.MinValue;
        if (!Preferences.HideOnFullscreen && fullscreenHidden) { fullscreenHidden = false; if (!hidden) Show(); }
        if (bubbleWindow.IsVisible) ResetDwell();
        Preferences.Left = appliedPosition.X; Preferences.Top = appliedPosition.Y; Preferences.Save();
    }
    static Button ActionButton(string label, Action action)
    {
        var button = new Button { Content = label, Padding = new Thickness(18, 9), CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 4, 10, 4), Background = Brushes.White, Foreground = Hair, BorderBrush = new SolidColorBrush(Color.FromRgb(222,226,237)), BorderThickness = new Thickness(1) };
        button.Click += (_, _) => action(); return button;
    }
    static TextBlock Paragraph(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 12), Foreground = Brushes.SlateGray };
    static Window Dialog(string title, Avalonia.Controls.Control content, double width = 630, double height = 460) => new() {
        Title = title, Width = width, Height = height, MinWidth = 480, MinHeight = 360,
        FontFamily = new FontFamily("Noto Sans CJK SC"), FontSize = 14,
        WindowStartupLocation = WindowStartupLocation.CenterScreen, Background = new SolidColorBrush(Color.FromRgb(247, 248, 251)),
        Content = new Border { Padding = new Thickness(28), Child = content } };
    async Task<bool> Confirm(string title, string message, string accept = "确定", CancellationToken token = default, string reject = "取消")
    {
        if (consentOpen) throw new InvalidOperationException("请先处理已打开的提示窗口。");
        consentOpen = true; bool accepted = false;
        try {
            var panel = new StackPanel { Spacing = 15 };
            panel.Children.Add(new TextBlock { Text = title, FontSize = 23, FontWeight = FontWeight.SemiBold }); panel.Children.Add(Paragraph(message));
            var dialog = Dialog(title, panel); var buttons = new WrapPanel();
            buttons.Children.Add(ActionButton(accept, () => { accepted = true; dialog.Close(); })); buttons.Children.Add(ActionButton(reject, dialog.Close)); panel.Children.Add(buttons);
            using var registration = token.Register(() => Dispatcher.UIThread.Post(() => { accepted = false; dialog.Close(); }));
            var finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            dialog.Closed += (_, _) => finished.TrySetResult(accepted && !token.IsCancellationRequested);
            consentWindow = dialog;
            if (settings?.IsVisible == true) dialog.Show(settings); else dialog.Show();
            return await finished.Task;
        } finally { consentWindow = null; consentOpen = false; }
    }
    void OpenSettings()
    {
        if (settings != null) { if (settings.WindowState == WindowState.Minimized) settings.WindowState = WindowState.Normal; settings.Activate(); return; }
        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("180,*") };
        var navigation = new StackPanel { Spacing = 10, Margin = new Thickness(0, 0, 20, 0) };
        var body = new StackPanel { Spacing = 8 }; var scroll = new ScrollViewer { Content = body }; Grid.SetColumn(scroll, 1); root.Children.Add(navigation); root.Children.Add(scroll);
        settings = Dialog("蓝色大肥鱼 · 设置", root, 880, 700); settings.MinWidth = 780; settings.MinHeight = 600;
        settings.Closed += (_, _) => { settings = null; refreshLinuxSettings = null; Save(); nextEvent = DateTime.UtcNow.AddSeconds(Preferences.AnimationIntervalSeconds); };
        Action selectedPage = General;
        void Navigate(Action page) { selectedPage = page; page(); }
        void Title(string text) { body.Children.Clear(); body.Children.Add(new TextBlock { Text = text, FontSize = 25, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 16) }); }
        void Toggle(string text, bool value, Action<bool> set) { var box = new CheckBox { Content = text, IsChecked = value, Margin = new Thickness(0, 4) }; box.IsCheckedChanged += (_, _) => { set(box.IsChecked == true); Save(); }; body.Children.Add(box); }
        void Slider(string text, double value, double low, double high, Action<double> set, double tick = 1) {
            var label = new TextBlock { Text = text + "：" + value.ToString("0.##"), Margin = new Thickness(0, 12, 0, 0) };
            var slider = new Avalonia.Controls.Slider { Minimum = low, Maximum = high, Value = value, TickFrequency = tick, IsSnapToTickEnabled = true };
            slider.ValueChanged += (_, _) => { label.Text = text + "：" + slider.Value.ToString("0.##"); set(slider.Value); Save(); }; body.Children.Add(label); body.Children.Add(slider);
        }
        ComboBox Select(string label, string[] choices, int selected, Action<int> set) {
            body.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 10, 0, 4) }); var combo = new ComboBox { ItemsSource = choices, SelectedIndex = selected, HorizontalAlignment = HorizontalAlignment.Stretch };
            combo.SelectionChanged += (_, _) => { if (combo.SelectedIndex >= 0) { set(combo.SelectedIndex); Save(); } }; body.Children.Add(combo); return combo;
        }
        void General() {
            Title("桌宠与显示"); Slider("桌宠大小（%）", Preferences.Scale * 100, 50, 200, value => { Preferences.Scale = value / 100; Rescale(); Clamp(); }, 5);
            Slider("文字气泡大小（%）", Preferences.BubbleScale * 100, 50, 200, value => { Preferences.BubbleScale = value / 100; MoveBubble(); }, 5);
            Toggle("开机启动", StartupEnabled, SetStartup); Toggle("全屏时自动隐藏桌宠", Preferences.HideOnFullscreen, value => { Preferences.HideOnFullscreen = value; ApplySettings(); });
            Select("气泡驻留时间", CompanionPreferences.BubbleDurationLabels, Preferences.BubbleDurationIndex, value => { Preferences.BubbleDurationIndex = value; ResetDwell(); });
        }
        void Model() {
            Title("模型与信息"); Toggle("启用本地大模型", Preferences.ModelEnabled, value => { Preferences.ModelEnabled = value; ApplySettings(); });
            Toggle("使用 GPU 加速", Preferences.GpuEnabled, value => { Preferences.GpuEnabled = value; ApplySettings(); });
            var deviceStatus = Paragraph(""); body.Children.Add(deviceStatus);
            var deviceClock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            void UpdateDevice() => deviceStatus.Text = "推理设备：" + brain.ExecutionDevice;
            deviceClock.Tick += (_, _) => UpdateDevice();
            deviceStatus.AttachedToVisualTree += (_, _) => { UpdateDevice(); deviceClock.Start(); };
            deviceStatus.DetachedFromVisualTree += (_, _) => deviceClock.Stop();
            Toggle("笔记本离电时暂停模型", Preferences.StopModelOnBattery, value => { Preferences.StopModelOnBattery = value; ApplySettings(); });
            Toggle("联网获取天气和科技新闻", Preferences.PublicInfo, value => Preferences.PublicInfo = value);
            Toggle("感知当前前台应用", Preferences.ForegroundEnabled, value => Preferences.ForegroundEnabled = value);
            body.Children.Add(ActionButton(brain.ModelAvailable ? "管理本地模型" : "安装模型", async () => await InstallModel()));
            var frequency = Select("自动说话间隔", CompanionPreferences.SpeechRanges.Select(r => $"{r.Min}–{r.Max} 分钟").ToArray(), Preferences.SpeechRangeIndex, value => { Preferences.SpeechMinMinutes = CompanionPreferences.SpeechRanges[value].Min; nextSpeech = DateTime.UtcNow.Add(Preferences.NextSpeechDelay(random)); });
            frequency.IsEnabled = !Puppet;
            body.Children.Add(Paragraph(Puppet ? "日常关心的触发时机已交给 Agent，此时说话间隔不可调节。关闭接管触发后恢复使用此设置。" : AgentDailyActive ? "鱼鱼按此间隔触发日常关心，由 Agent 编写吐字。" : "鱼鱼按此间隔触发日常关心，由本地模型编写吐字。"));
            body.Children.Add(new TextBlock { Text = "天气地区", Margin = new Thickness(0,12,0,4) });
            var regions = new StackPanel { Spacing = 8 }; body.Children.Add(regions);
            ComboBox province = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch }, city = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch }, district = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            regions.Children.Add(province); regions.Children.Add(city); regions.Children.Add(district);
            var selected = CompanionRegions.District(Preferences.WeatherRegionCode); var provinces = CompanionRegions.All.Where(r => r.Level == 0).ToArray();
            CompanionRegion[] cities = [], districts = []; bool changing = true;
            void PopulateCity(CompanionRegion choice, string? keep = null) { cities = CompanionRegions.Children(choice.Code); city.ItemsSource = cities.Select(r => r.Name).ToArray(); city.SelectedIndex = Math.Max(0, Array.FindIndex(cities,r => r.Code == keep)); }
            void PopulateDistrict(CompanionRegion choice, string? keep = null) { districts = CompanionRegions.Children(choice.Code); district.ItemsSource = districts.Select(r => r.Name).ToArray(); district.SelectedIndex = Math.Max(0, Array.FindIndex(districts,r => r.Code == keep)); }
            province.ItemsSource = provinces.Select(r => r.Name).ToArray(); province.SelectedIndex = Array.FindIndex(provinces,r => r.Code == CompanionRegions.Parent(CompanionRegions.Parent(selected)).Code);
            PopulateCity(provinces[province.SelectedIndex], CompanionRegions.Parent(selected).Code); PopulateDistrict(cities[city.SelectedIndex], selected.Code); changing = false;
            void RegionChanged() { if (district.SelectedIndex >= 0 && district.SelectedIndex < districts.Length) { Preferences.WeatherRegionCode = districts[district.SelectedIndex].Code; ApplySettings(); } }
            province.SelectionChanged += (_,_) => { if(changing || province.SelectedIndex < 0) return; changing = true; PopulateCity(provinces[province.SelectedIndex]); PopulateDistrict(cities[city.SelectedIndex]); changing = false; RegionChanged(); };
            city.SelectionChanged += (_,_) => { if(changing || city.SelectedIndex < 0) return; changing = true; PopulateDistrict(cities[city.SelectedIndex]); changing = false; RegionChanged(); };
            district.SelectionChanged += (_,_) => { if(!changing) RegionChanged(); };
        }
        void Animation() {
            Title("动作与作息"); Slider("动作触发间隔（秒）", Preferences.AnimationIntervalSeconds, 30, 300, value => { Preferences.AnimationIntervalSeconds = (int)value; nextEvent = DateTime.UtcNow.AddSeconds(value); });
            foreach (var meal in new[] { ("早餐", "BreakfastHour"), ("午餐", "LunchHour"), ("晚餐", "DinnerHour") }) {
                var property = typeof(CompanionPreferences).GetProperty(meal.Item2)!;
                Slider(meal.Item1 + "时间（时）", (int)property.GetValue(Preferences)!, 0, 23, value => property.SetValue(Preferences, (int)value));
            }
        }
        void About() {
            Title("关于与更新"); body.Children.Add(Paragraph("蓝色大肥鱼 " + typeof(LinuxPetWindow).Assembly.GetName().Version!.ToString(3) + (McpAvailable ? "" : " · 无MCP版")));
            Toggle("每次启动自动检查更新", Preferences.CheckUpdatesOnStartup, value => Preferences.CheckUpdatesOnStartup = value);
            body.Children.Add(ActionButton("检查更新", async () => await CheckUpdates(true)));
        }
        navigation.Children.Add(ActionButton("桌宠与显示", () => Navigate(General))); navigation.Children.Add(ActionButton("模型与信息", () => Navigate(Model))); navigation.Children.Add(ActionButton("动作与作息", () => Navigate(Animation)));
#if !COMPANION_SAFE
        void Agent() { Title("连接 Agent"); BuildAgentSettings(body); }
        navigation.Children.Add(ActionButton("连接 Agent", () => Navigate(Agent)));
#endif
        navigation.Children.Add(ActionButton("关于与更新", () => Navigate(About)));
        refreshLinuxSettings = () => { var offset = scroll.Offset; selectedPage(); scroll.Offset = offset; };
        General(); settings.Show(); settings.Activate();
    }
}


