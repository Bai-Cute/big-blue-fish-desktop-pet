using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace BigBlueFish.Windows;

public partial class MainWindow
{
    private const string BatteryPauseNotice = "主人，当前设置为笔记本离电暂停模型，可以在设置里调节哦。";

    private void BuildCompanionContextMenu()
    {
        companionContextMenu = new ContextMenu { PlacementTarget = this, Placement = PlacementMode.MousePoint };
        companionContextMenu.Resources.MergedDictionaries.Add(new ResourceDictionary
        { Source = new Uri("/BigBlueFish;component/Companion/CompanionMenuStyles.xaml", UriKind.Relative) });
        companionContextMenu.Style = (Style)companionContextMenu.FindResource(typeof(ContextMenu));
        companionMenuDismissal = new CompanionMenuDismissal(companionContextMenu);
        TextBlock Icon(string glyph) => new()
        {
            Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 16,
            Foreground = new SolidColorBrush(Color.FromRgb(80, 96, 152)),
            VerticalAlignment = VerticalAlignment.Center
        };
        var settings = new MenuItem { Header = "设置" };
        settings.Icon = Icon("\uE713");
        settings.Click += (_, _) => { companionContextMenu.IsOpen = false; OpenCompanionSettings(); };
        var weather = new MenuItem { Header = "天气" };
        weather.Icon = Icon("\uE706");
        weather.Click += async (_, _) => { companionContextMenu.IsOpen = false; await SpeakCompanionWeather(); };
        companionContextMenu.Items.Add(settings);
        companionContextMenu.Items.Add(weather);
        var test = new MenuItem { Header = "吐字测试" };
        test.Icon = Icon("\uE8F2");
        test.Click += async (_, _) => { companionContextMenu.IsOpen = false; await SpeakCompanionTest(); };
        companionContextMenu.Items.Add(test);
        var hide = new MenuItem { Header = "隐藏桌宠" };
        hide.Icon = Icon("\uE8A7");
        hide.Click += (_, _) => { companionContextMenu.IsOpen = false; HideCompanion(); };
        companionContextMenu.Items.Add(hide);
        companionContextMenu.Opened += (_, _) => { weather.IsEnabled = true; test.IsEnabled = true; };
    }

    private async Task SpeakCompanionWeather()
    {
        if (weatherRequestPending || companionClosed || exitAnimation) return;
        if (hiddenByUser || fullscreenHidden) RestoreCompanion();
        if (await TryManualAgentSpeech(true)) return;
        if (!brain.ModelAvailable) { OpenModelInstaller(); return; }
        if (hiddenByUser || fullscreenHidden) RestoreCompanion();
        if (!preferences.ModelEnabled)
        {
            ShowCompanionNotice("主人，请先在设置里启用本地大模型，再来查看天气吧～", manual: true);
            return;
        }
        if (!brain.CanRunOnCurrentPower)
        {
            ShowCompanionNotice(BatteryPauseNotice, true, true);
            return;
        }
        InvalidateSpeechRequests();
        weatherRequestPending = true;
        long version = speechRequestVersion;
        string regionCode = preferences.WeatherRegionCode;
        try
        {
            ShowCompanionNotice("主人，正在查看" + CompanionRegions.FullName(CompanionRegions.District(regionCode)) + "的天气～", manual: true);
            // Let an in-flight warm-up or utterance finish; automatic speech yields to this request.
            var deadline = DateTime.UtcNow.AddSeconds(100);
            while (brain.Busy && DateTime.UtcNow < deadline && !companionClosed && brain.CanRunOnCurrentPower)
                await Task.Delay(100);
            if (version != speechRequestVersion || companionClosed || !preferences.ModelEnabled || !brain.CanRunOnCurrentPower || regionCode != preferences.WeatherRegionCode)
                return;
            if (brain.Busy) { ShowCompanionNotice("主人，模型暂时还在忙，请稍后再试一下～", manual: true); return; }
            BeginModelSpeech();
            string text;
            try
            {
                text = await brain.GenerateWeather(partial => ShowCompanionPartial(partial, version, true));
            }
            finally
            {
                if (version == speechRequestVersion) EndModelSpeech();
            }
            if (version == speechRequestVersion && !companionClosed && !hiddenByUser && !fullscreenHidden && preferences.ModelEnabled
                && brain.CanRunOnCurrentPower && regionCode == preferences.WeatherRegionCode)
            {
                if (text.Length > 0) { FinishCompanionPartial(text, true); }
                else ShowCompanionNotice("主人，" + brain.Status + "。", manual: true);
            }
        }
        finally
        {
            if (version == speechRequestVersion) {
                weatherRequestPending = false;
                nextSpeech = DateTime.UtcNow.Add(preferences.NextSpeechDelay(companionRandom));
            }
        }
    }

    private async Task SpeakCompanionTest()
    {
        if (companionClosed || exitAnimation) return;
        if (hiddenByUser || fullscreenHidden) RestoreCompanion();
        if (await TryManualAgentSpeech(false)) return;
        if (!preferences.ModelEnabled)
        {
            ShowCompanionNotice("主人，当前设置暂停了模型，请启用模型并确认供电设置后再测试～", manual: true);
            return;
        }
        if (!brain.CanRunOnCurrentPower)
        {
            ShowCompanionNotice(BatteryPauseNotice, true, true);
            return;
        }
        // A test directly enters the same single handler used when the ordinary
        // speech countdown reaches zero.
        speechForegroundOverride = CompanionNative.ResolveSpeechForeground(CompanionNative.GetForegroundWindow(), lastExternalForeground);
        await GenerateLocalSpeechAsync(true);
    }

    private async Task<bool> TryManualAgentSpeech(bool weather)
    {
#if COMPANION_SAFE
        await Task.CompletedTask; return false;
#else
        if (!AgentDailyActive) return false;
        try
        {
            ShowCompanionNotice(weather ? "主人，正在准备天气资料交给 Agent～" : "主人，正在请 Agent 回应这次吐字测试～", true, true);
            string context = weather ? await brain.GetWeatherContext(System.Threading.CancellationToken.None) : "";
            if (companionClosed || exitAnimation) return true;
            if (agent?.ManualSpeech(weather, context) != true) ShowCompanionNotice("主人，Agent 请求尚未就绪或队列已满，请查看连接状态后重试～", true, true);
        }
        catch (Exception error) { ShowCompanionNotice("主人，" + error.Message, true, true); }
        return true;
#endif
    }

    private void AddCompanionRegionSelectors(Panel panel)
    {
        panel.Children.Add(new TextBlock { Text = "天气地点", Margin = new Thickness(0, 10, 0, 4) });
        var grid = new Grid();
        for (int i = 0; i < 4; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(i == 0 ? 0.7 : 1, GridUnitType.Star) });
        (ComboBox Combo, TextBlock Caption, StackPanel Column) Selector(string name, string caption, int column)
        {
            var columnPanel = new StackPanel { Margin = new Thickness(column == 0 ? 0 : 6, 0, 0, 0) };
            var label = new TextBlock { Text = caption, FontSize = 12, Margin = new Thickness(0, 0, 0, 4) };
            columnPanel.Children.Add(label);
            var combo = new ComboBox { Name = name, DisplayMemberPath = "Name", SelectedValuePath = "Code", MinHeight = 30 };
            System.Windows.Automation.AutomationProperties.SetName(combo, caption);
            columnPanel.Children.Add(combo);
            Grid.SetColumn(columnPanel, column);
            grid.Children.Add(columnPanel);
            return (combo, label, columnPanel);
        }
        var country = Selector("WeatherCountry", "国家", 0).Combo;
        var levels = new[]
        {
            Selector("WeatherProvince", "省 / 自治区 / 直辖市", 1),
            Selector("WeatherCity", "市 / 州", 2),
            Selector("WeatherDistrict", "县 / 区", 3),
        };
        bool changing = false;
        CompanionCountry current = CompanionRegions.China;
        void Configure(CompanionCountry target)
        {
            current = target;
            for (int i = 0; i < levels.Length; i++)
            {
                var visible = i <= target.LeafLevel;
                levels[i].Column.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                levels[i].Caption.Text = visible ? target.Captions[i] : "";
                System.Windows.Automation.AutomationProperties.SetName(levels[i].Combo, levels[i].Caption.Text);
            }
            // The leaf column absorbs the space of hidden levels.
            for (int i = 1; i < 4; i++) grid.ColumnDefinitions[i].Width = new GridLength(i - 1 <= target.LeafLevel ? 1 : 0, GridUnitType.Star);
        }
        // Fill level `from` and below; keep the matching chain entry when present, else the first item.
        void Fill(int from, string parentCode, CompanionRegion[] keep)
        {
            for (int i = from; i <= current.LeafLevel; i++)
            {
                var items = CompanionRegions.Children(parentCode);
                levels[i].Combo.ItemsSource = items;
                var index = Array.FindIndex(items, x => i < keep.Length && x.Code == keep[i].Code);
                levels[i].Combo.SelectedIndex = items.Length == 0 ? -1 : Math.Max(0, index);
                if (levels[i].Combo.SelectedItem is not CompanionRegion chosen) break;
                parentCode = chosen.Code;
            }
            for (int i = current.LeafLevel + 1; i < levels.Length; i++) levels[i].Combo.ItemsSource = Array.Empty<CompanionRegion>();
        }
        void SaveRegion()
        {
            if (levels[current.LeafLevel].Combo.SelectedItem is not CompanionRegion region) return;
            if (preferences.WeatherRegionCode != region.Code) InvalidateSpeechRequests();
            preferences.WeatherRegionCode = region.Code;
            brain.WeatherRegionCode = region.Code;
            SaveCompanion();
        }
        var selected = CompanionRegions.District(preferences.WeatherRegionCode);
        country.ItemsSource = CompanionRegions.Countries;
        country.SelectedItem = CompanionRegions.Country(selected);
        Configure(CompanionRegions.Country(selected));
        Fill(0, current.Root, CompanionRegions.Chain(selected));
        country.SelectionChanged += (_, _) =>
        {
            if (changing || country.SelectedItem is not CompanionCountry target || target == current) return;
            changing = true;
            Configure(target);
            Fill(0, target.Root, target == CompanionRegions.China ? CompanionRegions.Chain(CompanionRegions.District(CompanionRegions.DefaultCode)) : []);
            changing = false;
            SaveRegion();
        };
        for (int level = 0; level < levels.Length; level++)
        {
            var at = level;
            levels[at].Combo.SelectionChanged += (_, _) =>
            {
                if (changing || levels[at].Combo.SelectedItem is not CompanionRegion region) return;
                changing = true;
                if (at < current.LeafLevel) Fill(at + 1, region.Code, []);
                changing = false;
                SaveRegion();
            };
        }
        panel.Children.Add(grid);
        panel.Children.Add(new TextBlock { Text = "天气来自 Open-Meteo（Weather data by Open-Meteo.com），按所选区县中心附近估算；美国地点使用华氏度和英里/小时。", FontSize = 11,
            Foreground = System.Windows.Media.Brushes.SlateGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 4) });
    }
}
