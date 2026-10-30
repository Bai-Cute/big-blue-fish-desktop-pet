#if !COMPANION_SAFE
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using BigBlueFish.Agent;
using BigBlueFish.Control;

namespace BigBlueFish.Windows;

internal sealed partial class LinuxPetWindow
{
    void BuildAgentSettings(StackPanel body)
    {
        var enabled = new CheckBox { Content = "连接本机兼容的 CLI Harness", IsChecked = Preferences.AgentEnabled };
        body.Children.Add(enabled); body.Children.Add(Paragraph("通过已安装的 Agent 与鱼鱼对话，并控制鱼鱼的行为。"));
        var extra = new StackPanel { IsVisible = Preferences.AgentEnabled, Spacing = 8 }; body.Children.Add(extra);
        extra.Children.Add(new TextBlock { Text = "选择 Harness" });
        var select = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        var adapters = new AgentAdapterStore(Path.Combine(Environment.CurrentDirectory, "agent"));
        bool refreshing = false;
        void Detect()
        {
            refreshing = true;
            select.ItemsSource = HarnessCatalog.All.OrderByDescending(h => adapters.FindExecutable(h) != null)
                .Select(h => new ComboBoxItem { Content = h.Name + (adapters.FindExecutable(h) == null ? h.Id == "antigravity" ? " · 连接时准备官方组件" : " · 未检测到" : " · 已检测到"), Tag = h.Id }).ToArray();
            select.SelectedItem = select.Items.OfType<ComboBoxItem>().FirstOrDefault(x => (string?)x.Tag == Preferences.AgentHarnessId);
            refreshing = false;
        }
        Detect(); extra.Children.Add(select);
        var buttons = new WrapPanel(); buttons.Children.Add(ActionButton("重新检测", Detect));
        buttons.Children.Add(ActionButton("安装与登录说明", () => Process.Start(new ProcessStartInfo("xdg-open") { ArgumentList = { HarnessCatalog.Get(Preferences.AgentHarnessId).Website } })));
        extra.Children.Add(buttons);
        var state = Paragraph(""); extra.Children.Add(state);
        var actions = new WrapPanel();
        actions.Children.Add(ActionButton("重新连接", () => agent?.Reconnect()));
        actions.Children.Add(ActionButton("开始新对话", async () => { if (await Confirm("开始新对话", "将建立新的鱼鱼对话，后续使用新的上下文。")) agent?.NewConversation(); }));
        actions.Children.Add(ActionButton("取消当前请求", () => agent?.Cancel())); extra.Children.Add(actions);
        var refreshWorkspace = BuildAgentWorkspaceSettings(extra);
        var refreshConfiguration = BuildAgentConfigurationSettings(extra);
        var daily = new CheckBox { Content = ControlTools.DailySpeechLabel, IsChecked = Preferences.AgentDailySpeech }; extra.Children.Add(daily);
        extra.Children.Add(Paragraph(ControlTools.DailySpeechDescription));
        var puppet = new CheckBox { Content = ControlTools.BehaviorControlLabel, IsChecked = Preferences.PuppetMode }; extra.Children.Add(puppet);
        extra.Children.Add(Paragraph(ControlTools.BehaviorControlDescription));
        var wake = Paragraph(""); extra.Children.Add(wake);
        extra.Children.Add(ActionButton("编辑人设", () => {
            if (agent == null) return;
            var skills = agent.Skills; skills.Install();
            var editor = new TextBox { Text = File.ReadAllText(skills.PersonaPath), AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Height = 320 };
            var content = new StackPanel { Spacing = 15 }; content.Children.Add(Paragraph("保存后在下一轮消息中应用，可修改或清空。")); content.Children.Add(editor);
            var window = Dialog("鱼鱼人设", content, 700, 560);
            content.Children.Add(ActionButton("保存", () => { AtomicText.Write(skills.PersonaPath, editor.Text ?? ""); window.Close(); })); window.Show(settings!);
        }));
        var details = Paragraph(""); extra.Children.Add(new Expander { Header = "连接详情", Content = details });
        void Refresh()
        {
            refreshWorkspace(); refreshConfiguration();
            if (select.SelectedItem is ComboBoxItem { Tag: "antigravity" } selected)
                selected.Content = "Antigravity CLI" + (adapters.FindExecutable(HarnessCatalog.Get("antigravity")) == null ? " · 连接时准备官方组件" : " · 已检测到");
            state.Text = (agent?.Status ?? "尚未连接") + "\n" + (agent?.Detail ?? "选择后连接，双击鱼鱼即可对话。");
            details.Text = $"程序：{agent?.ExecutablePath}\n版本：{agent?.Version}\n模型：{agent?.CurrentModel}\n对话编号：{agent?.SessionId}\n内部工具连接：{mcpServer?.Connections ?? 0}\n点击响应：{Preferences.AgentRespondToClick}；拖动响应：{Preferences.AgentRespondToDrag}（可由 Agent 调整）";
            wake.Text = Preferences.PuppetMode && Preferences.AgentDailySpeech ? "下一次日常关心：" + (agent?.NextWake ?? "等待 Agent 安排")
                : Preferences.PuppetMode ? "Agent 安排日常关心的时机，本地模型负责吐字。下一次关心：" + (agent?.NextWake ?? "等待 Agent 预约") : Preferences.AgentDailySpeech ? "鱼鱼决定何时关心主人，Agent 编写关心的话。" : "鱼鱼按自己的节奏关心主人，日常说话使用内置模型。";
            if (Preferences.PuppetMode && agent?.Connected != true) wake.Text += "\nAgent 未连接，鱼鱼保持待机；关闭第二个开关可恢复鱼鱼自己的安排。";
        }
        select.SelectionChanged += (_, _) => { if (!refreshing && select.SelectedItem is ComboBoxItem { Tag: string id }) { Preferences.AgentHarnessId = id; ConfigureAgent(); Save(); } };
        daily.Click += (_, _) => { Preferences.AgentDailySpeech = daily.IsChecked == true; ApplySettings(); Refresh(); };
        puppet.Click += async (_, _) => { bool on = puppet.IsChecked == true; if (on && !await Confirm(ControlTools.BehaviorControlTitle, ControlTools.PuppetWarning, "了解并开启")) { puppet.IsChecked = false; return; } SetPuppet(on); Save(); Refresh(); };
        enabled.Click += async (_, _) => {
            enabled.IsEnabled = false;
            try
            {
                bool on = enabled.IsChecked == true;
                if (on && !await Confirm("连接本机 Agent", ControlTools.PrivacyWarning, "了解并开启")) { enabled.IsChecked = false; return; }
                if (!on) SetPuppet(false); Preferences.AgentEnabled = on;
                if (on) InitializeMcp(); else StopMcp();
                extra.IsVisible = on; puppet.IsChecked = Preferences.PuppetMode; Save(); Refresh();
            }
            finally { enabled.IsEnabled = true; }
        };
        var clock = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        clock.Tick += (_, _) => Refresh(); extra.DetachedFromVisualTree += (_, _) => clock.Stop(); Refresh(); clock.Start();
    }

    async Task<string?> RequestAgentPermission(AgentPermission request, CancellationToken token)
    {
        return await Dispatcher.UIThread.InvokeAsync(async () => {
            if (closed || !Preferences.AgentEnabled || token.IsCancellationRequested) return null;
            var content = new StackPanel { Spacing = 12 };
            string heading = request.Title.Length > 80 || request.Title.StartsWith('{') ? "Agent 请求执行操作" : request.Title;
            content.Children.Add(new TextBlock { Text = heading, FontSize = 22, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
            content.Children.Add(new ScrollViewer { Content = Paragraph(request.Detail.Length > 8000 ? request.Detail[..8000] : request.Detail), MaxHeight = 280 });
            var window = Dialog(request.Kind == "authentication" ? "Agent 登录认证" : "Agent 请求权限", content, 700, 500); var choices = new WrapPanel(); content.Children.Add(choices);
            string? selected = null;
            var complete = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            foreach (var option in request.Options) choices.Children.Add(ActionButton(option.Name, () => { selected = option.Id; window.Close(); }));
            choices.Children.Add(ActionButton("取消", window.Close));
            window.Closed += (_, _) => complete.TrySetResult(token.IsCancellationRequested ? null : selected);
            using var registration = token.Register(() => Dispatcher.UIThread.Post(window.Close));
            if (settings?.IsVisible == true) window.Show(settings); else window.Show();
            return await complete.Task;
        });
    }
}
#endif
