using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace VPet_Simulator.Windows;

public partial class MainWindow
{
    private void BuildCompanionContextMenu()
    {
        companionContextMenu = new ContextMenu { PlacementTarget = this, Placement = PlacementMode.MousePoint };
        var settings = new MenuItem { Header = "设置" };
        settings.Click += (_, _) => { companionContextMenu.IsOpen = false; OpenCompanionSettings(); };
        var weather = new MenuItem { Header = "天气" };
        weather.Click += async (_, _) => { companionContextMenu.IsOpen = false; await SpeakCompanionWeather(); };
        companionContextMenu.Items.Add(settings);
        companionContextMenu.Items.Add(weather);
        var test = new MenuItem { Header = "吐字测试" };
        test.Click += async (_, _) => { companionContextMenu.IsOpen = false; await SpeakCompanionTest(); };
        companionContextMenu.Items.Add(test);
    }

    private async Task SpeakCompanionWeather()
    {
        if (weatherRequestPending || speechTestPending || companionClosed) return;
        if (hiddenByUser) RestoreCompanion();
        if (!preferences.ModelEnabled)
        {
            ShowCompanionNotice("主人，请先在设置里启用本地大模型，再来查看天气吧～");
            return;
        }
        if (!brain.CanRunOnCurrentPower)
        {
            ShowCompanionNotice("主人，现在已按设置暂停模型。插上电，或关闭拔电暂停选项后，就能播报天气啦～");
            return;
        }
        weatherRequestPending = true;
        long version = ++speechRequestVersion;
        string regionCode = preferences.WeatherRegionCode;
        try
        {
            ShowCompanionNotice("主人，正在查看" + CompanionRegions.FullName(CompanionRegions.District(regionCode)) + "的天气～");
            // Let an in-flight warm-up or utterance finish; automatic speech yields to this request.
            var deadline = DateTime.UtcNow.AddSeconds(100);
            while (brain.Busy && DateTime.UtcNow < deadline && !companionClosed && brain.CanRunOnCurrentPower)
                await Task.Delay(100);
            if (version != speechRequestVersion || companionClosed || !preferences.ModelEnabled || !brain.CanRunOnCurrentPower || regionCode != preferences.WeatherRegionCode)
                return;
            if (brain.Busy) { ShowCompanionNotice("主人，模型暂时还在忙，请稍后再试一下～"); return; }
            var text = await brain.GenerateWeather();
            if (version == speechRequestVersion && !companionClosed && !hiddenByUser && !fullscreenHidden && preferences.ModelEnabled
                && brain.CanRunOnCurrentPower && regionCode == preferences.WeatherRegionCode)
            {
                if (text.Length > 0) { ShowCompanionBubble(text); PlayCompanion("wave"); }
                else ShowCompanionNotice("主人，" + brain.Status + "。");
            }
        }
        finally
        {
            weatherRequestPending = false;
            nextSpeech = DateTime.UtcNow.AddMinutes(preferences.SpeechMinMinutes);
        }
    }

    private async Task SpeakCompanionTest()
    {
        if (speechTestPending || companionClosed) return;
        if (hiddenByUser) RestoreCompanion();
        if (!preferences.ModelEnabled || !brain.CanRunOnCurrentPower)
        {
            ShowCompanionNotice("主人，当前设置暂停了模型，请启用模型并确认供电设置后再测试～");
            return;
        }
        speechTestPending = true;
        long version = ++speechRequestVersion;
        try
        {
            ShowCompanionNotice("主人，正在等待本地模型生成测试短句（" + (brain.UseGpu ? "显卡加速" : "CPU") + "）～");
            var deadline = DateTime.UtcNow.AddSeconds(100);
            while (brain.Busy && DateTime.UtcNow < deadline && !companionClosed && brain.CanRunOnCurrentPower)
                await Task.Delay(100);
            if (companionClosed || !preferences.ModelEnabled || !brain.CanRunOnCurrentPower) return;
            if (brain.Busy) { ShowCompanionNotice("主人，模型暂时还在忙，请稍后再试一下～"); return; }
            // A real ordinary model generation, using the same GPU/CPU configuration as normal speech.
            var text = await brain.Generate(false, false);
            if (version == speechRequestVersion && !companionClosed && !hiddenByUser && !fullscreenHidden
                && preferences.ModelEnabled && brain.CanRunOnCurrentPower)
            {
                if (text.Length > 0) { ShowCompanionBubble(text); PlayCompanion("wave"); }
                else ShowCompanionNotice("主人，吐字测试未能生成文字：" + brain.Status + "。");
            }
        }
        finally
        {
            speechTestPending = false;
            nextSpeech = DateTime.UtcNow.AddMinutes(preferences.SpeechMinMinutes);
        }
    }

    private void AddCompanionRegionSelectors(Panel panel)
    {
        panel.Children.Add(new TextBlock { Text = "天气地点", Margin = new Thickness(0, 10, 0, 4) });
        var grid = new Grid();
        for (int i = 0; i < 3; i++) grid.ColumnDefinitions.Add(new ColumnDefinition());
        ComboBox Selector(string name, string caption, int column)
        {
            var columnPanel = new StackPanel { Margin = new Thickness(column == 0 ? 0 : 6, 0, 0, 0) };
            columnPanel.Children.Add(new TextBlock { Text = caption, FontSize = 12, Margin = new Thickness(0, 0, 0, 4) });
            var combo = new ComboBox { Name = name, DisplayMemberPath = "Name", SelectedValuePath = "Code", MinHeight = 30 };
            System.Windows.Automation.AutomationProperties.SetName(combo, caption);
            columnPanel.Children.Add(combo);
            Grid.SetColumn(columnPanel, column);
            grid.Children.Add(columnPanel);
            return combo;
        }
        var province = Selector("WeatherProvince", "省 / 自治区 / 直辖市", 0);
        var city = Selector("WeatherCity", "市 / 州", 1);
        var district = Selector("WeatherDistrict", "县 / 区", 2);
        var selected = CompanionRegions.District(preferences.WeatherRegionCode);
        var selectedCity = CompanionRegions.Parent(selected);
        province.ItemsSource = CompanionRegions.Children("0");
        province.SelectedValue = selectedCity.ParentCode;
        city.ItemsSource = CompanionRegions.Children(selectedCity.ParentCode);
        city.SelectedValue = selectedCity.Code;
        district.ItemsSource = CompanionRegions.Children(selectedCity.Code);
        district.SelectedValue = selected.Code;
        bool changing = false;
        void SaveRegion()
        {
            if (district.SelectedItem is not CompanionRegion region) return;
            if (preferences.WeatherRegionCode != region.Code) brain.CancelGeneration();
            preferences.WeatherRegionCode = region.Code;
            brain.WeatherRegionCode = region.Code;
            SaveCompanion();
        }
        province.SelectionChanged += (_, _) =>
        {
            if (changing || province.SelectedItem is not CompanionRegion region) return;
            changing = true;
            city.ItemsSource = CompanionRegions.Children(region.Code);
            city.SelectedIndex = 0;
            district.ItemsSource = city.SelectedItem is CompanionRegion c ? CompanionRegions.Children(c.Code) : Array.Empty<CompanionRegion>();
            district.SelectedIndex = 0;
            changing = false;
            SaveRegion();
        };
        city.SelectionChanged += (_, _) =>
        {
            if (changing || city.SelectedItem is not CompanionRegion region) return;
            changing = true;
            district.ItemsSource = CompanionRegions.Children(region.Code);
            district.SelectedIndex = 0;
            changing = false;
            SaveRegion();
        };
        district.SelectionChanged += (_, _) => { if (!changing) SaveRegion(); };
        panel.Children.Add(grid);
        panel.Children.Add(new TextBlock { Text = "天气来自 Open-Meteo，按所选区县中心附近估算。", FontSize = 11,
            Foreground = System.Windows.Media.Brushes.SlateGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 4) });
    }
}
