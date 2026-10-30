#if !COMPANION_SAFE
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BigBlueFish.Agent;
using BigBlueFish.Control;
using BigBlueFish.Deployment;

namespace BigBlueFish.Windows;

public partial class MainWindow
{
    private Action AddMcpSettings(StackPanel panel, Window owner)
    {
        var enabled = new CheckBox { Content = "连接本机兼容的 CLI Harness", IsChecked = preferences.AgentEnabled, Margin = new(0, 12, 0, 5) };
        panel.Children.Add(enabled);
        panel.Children.Add(ModernUI.Text("通过已安装的 Agent 与鱼鱼对话，并控制鱼鱼的行为。"));
        var extra = new StackPanel { Visibility = preferences.AgentEnabled ? Visibility.Visible : Visibility.Collapsed };
        panel.Children.Add(extra);
        extra.Children.Add(new TextBlock { Text = "选择 Harness", FontWeight = FontWeights.SemiBold, Margin = new(0, 8, 0, 8) });
        var select = new ComboBox { MinHeight = 38, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        var adapters = new AgentAdapterStore(Path.Combine(Environment.CurrentDirectory, "agent"));
        bool updating = false;
        void Detect()
        {
            updating = true; select.Items.Clear();
            foreach (var h in HarnessCatalog.All.OrderByDescending(h => adapters.FindExecutable(h) != null))
            {
                var item = new ComboBoxItem { Content = h.Name + (adapters.FindExecutable(h) == null ? h.Id == "antigravity" ? " · 连接时准备官方组件" : " · 未检测到" : " · 已检测到"), Tag = h.Id };
                select.Items.Add(item); if (h.Id == preferences.AgentHarnessId) select.SelectedItem = item;
            }
            updating = false;
        }
        Detect(); extra.Children.Add(select);
        var detectRow = new WrapPanel();
        detectRow.Children.Add(ModernUI.Button("重新检测", Detect));
        detectRow.Children.Add(ModernUI.Button("安装与登录说明", () => {
            Process.Start(new ProcessStartInfo(HarnessCatalog.Get(preferences.AgentHarnessId).Website) { UseShellExecute = true });
        }));
        extra.Children.Add(detectRow);
        var state = new TextBlock { Foreground = ModernUI.Blue, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 5) };
        var description = ModernUI.Text(""); extra.Children.Add(state); extra.Children.Add(description);
        var actions = new WrapPanel();
        actions.Children.Add(ModernUI.Button("重新连接", () => agent?.Reconnect()));
        actions.Children.Add(ModernUI.Button("开始新对话", async () => {
            if (await ConfirmMcp(owner, "开始新对话", "将建立新的鱼鱼对话，后续消息使用新的上下文。原对话由 Agent 自行保留。")) agent?.NewConversation();
        }));
        actions.Children.Add(ModernUI.Button("取消当前请求", () => agent?.Cancel()));
        extra.Children.Add(actions);
        var refreshWorkspace = AddAgentWorkspaceSettings(extra, owner);
        var refreshConfiguration = AddAgentConfigurationSettings(extra, owner);
        var daily = new CheckBox { Content = ControlTools.DailySpeechLabel, IsChecked = preferences.AgentDailySpeech, Margin = new(0, 18, 0, 5) };
        extra.Children.Add(daily); extra.Children.Add(ModernUI.Text(ControlTools.DailySpeechDescription));
        var puppet = new CheckBox { Content = ControlTools.BehaviorControlLabel, IsChecked = preferences.PuppetMode, Margin = new(0, 10, 0, 5) };
        extra.Children.Add(puppet); extra.Children.Add(ModernUI.Text(ControlTools.BehaviorControlDescription));
        var wake = ModernUI.Text(""); extra.Children.Add(wake);
        extra.Children.Add(ModernUI.Button("编辑人设", () => EditAgentPersona(owner)));
        var details = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.SlateGray };
        extra.Children.Add(new Expander { Header = "连接详情", Content = details, Margin = new(0, 10, 0, 8) });
        var clock = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        void Refresh()
        {
            refreshWorkspace(); refreshConfiguration();
            if (select.SelectedItem is ComboBoxItem { Tag: "antigravity" } selected)
                selected.Content = "Antigravity CLI" + (adapters.FindExecutable(HarnessCatalog.Get("antigravity")) == null ? " · 连接时准备官方组件" : " · 已检测到");
            state.Text = agent?.Status ?? "尚未连接";
            description.Text = agent?.Detail ?? "选择已安装的 Harness，连接后双击鱼鱼即可对话。";
            details.Text = $"程序：{agent?.ExecutablePath ?? "尚未启动"}\nAgent 版本：{agent?.Version}\n模型：{agent?.CurrentModel}\n对话编号：{agent?.SessionId}\n内部工具连接：{mcpServer?.Connections ?? 0}\n输入会发起对话；点击响应：{preferences.AgentRespondToClick}；拖动响应：{preferences.AgentRespondToDrag}（可由 Agent 调整）";
            wake.Text = preferences.PuppetMode && preferences.AgentDailySpeech
                ? "下一次日常关心：" + (agent?.NextWake ?? "等待 Agent 安排")
                : preferences.PuppetMode ? "Agent 安排日常关心的时机，本地模型负责吐字。下一次关心：" + (agent?.NextWake ?? "等待 Agent 预约") : preferences.AgentDailySpeech ? "鱼鱼决定何时关心主人，Agent 编写关心的话。" : "鱼鱼按自己的节奏关心主人，日常说话使用内置模型。";
            if (preferences.PuppetMode && agent?.Connected != true) wake.Text += "\nAgent 未连接，鱼鱼保持待机；关闭第二个开关可恢复鱼鱼自己的安排。";
        }
        select.SelectionChanged += (_, _) => {
            if (updating || select.SelectedItem is not ComboBoxItem { Tag: string id }) return;
            preferences.AgentHarnessId = id; ConfigureAgent(); SaveCompanion(); Refresh();
        };
        daily.Click += (_, _) => {
            preferences.AgentDailySpeech = daily.IsChecked == true;
            InvalidateSpeechRequests(true); ConfigureAgent(); nextSpeech = DateTime.UtcNow.Add(preferences.NextSpeechDelay(companionRandom)); SaveCompanion(); Refresh();
        };
        puppet.Click += async (_, _) => {
            puppet.IsEnabled = false;
            try
            {
                bool on = puppet.IsChecked == true;
                if (on && !await ConfirmMcp(owner, ControlTools.BehaviorControlTitle, ControlTools.PuppetWarning)) { puppet.IsChecked = false; return; }
                SetPuppetMode(on); SaveCompanion(); Refresh();
            }
            finally { puppet.IsEnabled = true; }
        };
        enabled.Click += async (_, _) => {
            enabled.IsEnabled = false;
            try
            {
                bool on = enabled.IsChecked == true;
                if (on && !await ConfirmMcp(owner, "连接本机 Agent", ControlTools.PrivacyWarning)) { enabled.IsChecked = false; return; }
                if (!on) SetPuppetMode(false);
                preferences.AgentEnabled = on;
                if (on) InitializeMcp(); else StopMcp();
                extra.Visibility = on ? Visibility.Visible : Visibility.Collapsed; puppet.IsChecked = preferences.PuppetMode;
                SaveCompanion(); Refresh();
            }
            catch (Exception e) { ModernUI.Error(owner, "连接设置失败", e); }
            finally { enabled.IsEnabled = true; }
        };
        clock.Tick += (_, _) => Refresh(); Refresh(); clock.Start(); return clock.Stop;
    }

    private void EditAgentPersona(Window owner)
    {
        if (agent == null) return;
        var skills = agent.Skills; skills.Install();
        var dialog = new ModernWindow("鱼鱼人设", "鱼鱼人设", "保存后，在 Agent 的下一轮消息中应用；可修改或清空。", 700, 620) { Owner = owner };
        var editor = new TextBox { Text = File.ReadAllText(skills.PersonaPath), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 320, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        dialog.Body.Children.Add(editor);
        dialog.Actions.Children.Add(ModernUI.Button("保存", () => { AtomicText.Write(skills.PersonaPath, editor.Text); dialog.Close(); }, true));
        dialog.Actions.Children.Add(ModernUI.Button("取消", dialog.Close)); dialog.Show();
    }
    private async Task<string?> RequestAgentPermission(AgentPermission request, CancellationToken token)
    {
        return await Dispatcher.InvokeAsync(async () => {
            if (companionClosed || !preferences.AgentEnabled || token.IsCancellationRequested) return null;
            string heading = request.Title.Length > 80 || request.Title.StartsWith('{') ? "Agent 请求执行操作" : request.Title;
            var dialog = new ModernWindow(request.Kind == "authentication" ? "Agent 登录认证" : "Agent 请求权限", heading,
                request.Detail.Length > 8000 ? request.Detail[..8000] : request.Detail, 700, 540);
            if (companionSettings?.IsVisible == true) dialog.Owner = companionSettings;
            string? selected = null;
            var finished = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            foreach (var option in request.Options)
                dialog.Actions.Children.Add(ModernUI.Button(option.Name, () => { selected = option.Id; dialog.Close(); }));
            dialog.Actions.Children.Add(ModernUI.Button("取消", dialog.Close));
            dialog.Closed += (_, _) => finished.TrySetResult(token.IsCancellationRequested ? null : selected);
            using var registration = token.Register(() => Dispatcher.BeginInvoke(() => dialog.Close()));
            dialog.Show(); return await finished.Task;
        }).Task.Unwrap();
    }
}
#endif
