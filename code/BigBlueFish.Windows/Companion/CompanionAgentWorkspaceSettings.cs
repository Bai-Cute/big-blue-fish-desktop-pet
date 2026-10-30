#if !COMPANION_SAFE
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using BigBlueFish.Agent;
using BigBlueFish.Deployment;
using Forms = System.Windows.Forms;

namespace BigBlueFish.Windows;

public partial class MainWindow
{
    private sealed class AgentFolderOwner(IntPtr handle) : Forms.IWin32Window { public IntPtr Handle => handle; }
    private static string? SelectAgentFolder(Window owner, string title, string initial)
    {
        using var picker = new Forms.FolderBrowserDialog { Description = title, UseDescriptionForTitle = true,
            InitialDirectory = Directory.Exists(initial) ? initial : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), ShowNewFolderButton = true };
        return picker.ShowDialog(new AgentFolderOwner(new WindowInteropHelper(owner).Handle)) == Forms.DialogResult.OK ? picker.SelectedPath : null;
    }
    private Action AddAgentWorkspaceSettings(StackPanel panel, Window owner)
    {
        panel.Children.Add(new TextBlock { Text = "工作区", FontWeight = FontWeights.SemiBold, Margin = new(0, 20, 0, 6) });
        var path = ModernUI.Text(""); path.TextWrapping = TextWrapping.Wrap; panel.Children.Add(path);
        panel.Children.Add(ModernUI.Text("文件由各 Harness 共享；新建对话和项目仍使用当前目录。切换工作区会结束当前请求。"));
        var buttons = new WrapPanel(); panel.Children.Add(buttons); bool operating = false;
        async Task Run(Func<Task> action)
        {
            if (operating) return; operating = true;
            try { await action(); }
            catch (OperationCanceledException) { }
            catch (Exception error) { ModernUI.Error(owner, "工作区操作失败", error); }
            finally { operating = false; }
        }
        buttons.Children.Add(ModernUI.Button("新建工作区", () => {
            if (agent == null) return;
            var coordinator = agent;
            string parent = Directory.GetParent(coordinator.Workspaces.Current)?.FullName ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var dialog = new ModernWindow("新建工作区", "新建工作区", "创建独立文件夹，并将鱼鱼切换到这里工作。", 650, 440) { Owner = owner };
            var name = new TextBox { Text = "新工作区", Margin = new(0, 8, 0, 12) };
            var location = ModernUI.Text(parent);
            dialog.Body.Children.Add(ModernUI.Text("文件夹名称")); dialog.Body.Children.Add(name);
            dialog.Body.Children.Add(location);
            dialog.Body.Children.Add(ModernUI.Button("选择位置", () => { var selected = SelectAgentFolder(dialog, "选择新工作区的存放位置", parent); if (selected != null) { parent = selected; location.Text = parent; } }));
            var create = ModernUI.Button("创建并打开", async () => {
                await Run(async () => { await coordinator.CreateWorkspace(parent, name.Text); dialog.Close(); });
            }, true);
            dialog.Actions.Children.Add(create); dialog.Actions.Children.Add(ModernUI.Button("取消", dialog.Close)); dialog.Show();
        }));
        buttons.Children.Add(ModernUI.Button("打开工作区", async () => {
            if (agent == null) return;
            var coordinator = agent; var selected = SelectAgentFolder(owner, "选择已有工作区", coordinator.Workspaces.Current);
            if (selected != null) await Run(() => coordinator.OpenWorkspace(selected));
        }));
        buttons.Children.Add(ModernUI.Button("查看文件夹", () => {
            try { if (agent != null) { agent.Workspaces.EnsureCurrent(); Process.Start(new ProcessStartInfo(agent.Workspaces.Current) { UseShellExecute = true }); } }
            catch (Exception error) { ModernUI.Error(owner, "无法打开文件夹", error); }
        }));
        buttons.Children.Add(ModernUI.Button("清空工作区", async () => {
            if (agent == null) return;
            var coordinator = agent; string expected = coordinator.Workspaces.Current;
            await Run(async () => {
                coordinator.Workspaces.ValidateClear(expected);
                if (await ConfirmMcp(owner, "清空工作区", $"将删除以下工作区内的全部文件和子文件夹：\n\n{expected}\n\n清空后重新开始鱼鱼对话，并重新准备默认操作技能与人设。", accept: "清空"))
                    await coordinator.ClearWorkspace(expected);
            });
        }));
        return () => { path.Text = agent?.Workspaces.Current ?? "连接后准备默认工作区。"; buttons.IsEnabled = agent != null && !operating && !agent.ChangingWorkspace; };
    }
    private Action AddAgentConfigurationSettings(StackPanel panel, Window owner)
    {
        panel.Children.Add(new TextBlock { Text = "模型与权限", FontWeight = FontWeights.SemiBold, Margin = new(0, 20, 0, 6) });
        var rows = new StackPanel(); panel.Children.Add(rows);
        panel.Children.Add(ModernUI.Text("选项及作用范围由当前 Harness 提供；权限／工作模式决定它如何执行任务与请求确认。"));
        AgentCoordinator? rendered = null; int revision = -1; bool applying = false; Action? refresh = null;
        async Task Apply(AgentCoordinator current, string id, string value)
        {
            if (applying) return; applying = true; rows.IsEnabled = false;
            try { await current.SetConfiguration(id, value); }
            catch (OperationCanceledException) { }
            catch (Exception error) { ModernUI.Error(owner, "Agent 配置未更改", error); }
            finally { applying = false; revision = -1; refresh?.Invoke(); }
        }
        refresh = () => {
            var current = agent; rows.IsEnabled = !applying && current?.Connected == true && !current.Busy && !current.ChangingWorkspace;
            if (applying || ReferenceEquals(rendered, current) && revision == (current?.Configuration.Revision ?? 0)) return;
            rendered = current; revision = current?.Configuration.Revision ?? 0; rows.Children.Clear();
            var options = current?.Configuration.Options ?? [];
            if (options.Length == 0) { rows.Children.Add(ModernUI.Text(current?.Connected == true ? "模型、权限和工作模式由 Harness 配置；此连接尚未提供可调整的选项。" : "连接后显示 Agent 提供的模型、权限和思考选项。")); return; }
            if (!options.Any(o => o.Category == "model")) rows.Children.Add(ModernUI.Text("当前模型：" + current!.CurrentModel));
            foreach (var option in options)
            {
                string label = option.Category switch { "model" => "模型 · ", "mode" => "权限／工作模式 · ", "thought_level" => "思考深度 · ", _ => "" };
                rows.Children.Add(ModernUI.Text(label + option.Name));
                if (option.Description.Length > 0) rows.Children.Add(ModernUI.Text(option.Description));
                if (option.Method.Length == 0) { rows.Children.Add(ModernUI.Text(option.CurrentValue + " · 由 Harness 配置")); continue; }
                if (option.Type == "boolean")
                {
                    var toggle = new CheckBox { Content = option.Name, IsChecked = option.CurrentValue == "true", Margin = new(0, 6, 0, 8) };
                    toggle.Click += async (_, _) => await Apply(current!, option.Id, toggle.IsChecked == true ? "true" : "false"); rows.Children.Add(toggle);
                }
                else
                {
                    var selector = new ComboBox { MinHeight = 34, HorizontalContentAlignment = HorizontalAlignment.Stretch, Margin = new(0, 5, 0, 8) };
                    foreach (var choice in option.Choices) selector.Items.Add(new ComboBoxItem { Content = (choice.Group.Length > 0 ? choice.Group + " · " : "") + choice.Name, Tag = choice.Value, ToolTip = choice.Description });
                    selector.SelectedItem = selector.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string?)i.Tag == option.CurrentValue);
                    if (selector.SelectedItem == null) { var actual = new ComboBoxItem { Content = option.CurrentValue.Length > 0 ? option.CurrentValue : "由 Harness 配置", Tag = option.CurrentValue, IsEnabled = false }; selector.Items.Add(actual); selector.SelectedItem = actual; }
                    selector.SelectionChanged += async (_, _) => { if (selector.SelectedItem is ComboBoxItem { Tag: string value } && value != option.CurrentValue) await Apply(current!, option.Id, value); };
                    rows.Children.Add(selector);
                }
            }
        };
        return refresh;
    }
}
#endif
