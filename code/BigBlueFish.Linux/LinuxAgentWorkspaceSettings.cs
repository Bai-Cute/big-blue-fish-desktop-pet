#if !COMPANION_SAFE
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using BigBlueFish.Agent;

namespace BigBlueFish.Windows;

internal sealed partial class LinuxPetWindow
{
    void AgentError(string title, Exception error)
    {
        var body = new StackPanel { Spacing = 15 }; body.Children.Add(Paragraph(error.Message));
        var dialog = Dialog(title, body); body.Children.Add(ActionButton("关闭", dialog.Close));
        if (settings?.IsVisible == true) dialog.Show(settings); else dialog.Show();
    }
    static async Task<string?> SelectAgentFolder(Window owner, string title, string initial)
    {
        var suggested = await owner.StorageProvider.TryGetFolderFromPathAsync(initial);
        var found = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false, SuggestedStartLocation = suggested });
        return found.FirstOrDefault()?.TryGetLocalPath();
    }
    Action BuildAgentWorkspaceSettings(StackPanel panel)
    {
        panel.Children.Add(Paragraph("工作区")); var path = Paragraph(""); panel.Children.Add(path);
        panel.Children.Add(Paragraph("文件由各 Harness 共享；新建对话和项目仍使用当前目录。切换工作区会结束当前请求。"));
        var buttons = new WrapPanel(); panel.Children.Add(buttons); bool operating = false;
        async Task Run(Func<Task> action)
        {
            if (operating) return; operating = true;
            try { await action(); } catch (OperationCanceledException) { }
            catch (Exception error) { AgentError("工作区操作失败", error); }
            finally { operating = false; }
        }
        buttons.Children.Add(ActionButton("新建工作区", () => {
            if (agent == null || settings == null) return;
            var current = agent; string parent = Directory.GetParent(current.Workspaces.Current)?.FullName ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var body = new StackPanel { Spacing = 12 }; body.Children.Add(Paragraph("创建独立文件夹，并将鱼鱼切换到这里工作。"));
            var name = new TextBox { Text = "新工作区", PlaceholderText = "文件夹名称" }; body.Children.Add(name);
            var pathText = Paragraph(parent); body.Children.Add(pathText);
            var dialog = Dialog("新建工作区", body, 650, 440);
            body.Children.Add(ActionButton("选择位置", async () => { var selected = await SelectAgentFolder(dialog, "选择新工作区的存放位置", parent); if (selected != null) { parent = selected; pathText.Text = parent; } }));
            var actions = new WrapPanel(); body.Children.Add(actions);
            actions.Children.Add(ActionButton("创建并打开", async () => await Run(async () => { await current.CreateWorkspace(parent, name.Text ?? ""); dialog.Close(); })));
            actions.Children.Add(ActionButton("取消", dialog.Close)); dialog.Show(settings);
        }));
        buttons.Children.Add(ActionButton("打开工作区", async () => {
            if (agent == null || settings == null) return;
            var current = agent; var selected = await SelectAgentFolder(settings, "选择已有工作区", current.Workspaces.Current);
            if (selected != null) await Run(() => current.OpenWorkspace(selected));
        }));
        buttons.Children.Add(ActionButton("查看文件夹", () => {
            try { if (agent != null) { agent.Workspaces.EnsureCurrent(); Process.Start(new ProcessStartInfo("xdg-open") { ArgumentList = { agent.Workspaces.Current } }); } }
            catch (Exception error) { AgentError("无法打开文件夹", error); }
        }));
        buttons.Children.Add(ActionButton("清空工作区", async () => {
            if (agent == null) return; var current = agent; string expected = current.Workspaces.Current;
            await Run(async () => {
                current.Workspaces.ValidateClear(expected);
                if (await Confirm("清空工作区", $"将删除以下工作区内的全部文件和子文件夹：\n\n{expected}\n\n清空后重新开始鱼鱼对话，并重新准备默认操作技能与人设。", "清空")) await current.ClearWorkspace(expected);
            });
        }));
        return () => { path.Text = agent?.Workspaces.Current ?? "连接后准备默认工作区。"; buttons.IsEnabled = agent != null && !operating && !agent.ChangingWorkspace; };
    }
    Action BuildAgentConfigurationSettings(StackPanel panel)
    {
        panel.Children.Add(Paragraph("模型与权限")); var rows = new StackPanel { Spacing = 8 }; panel.Children.Add(rows);
        panel.Children.Add(Paragraph("选项及作用范围由当前 Harness 提供；权限／工作模式决定它如何执行任务与请求确认。"));
        AgentCoordinator? rendered = null; int revision = -1; bool applying = false; Action? refresh = null;
        async Task Apply(AgentCoordinator current, string id, string value)
        {
            if (applying) return; applying = true; rows.IsEnabled = false;
            try { await current.SetConfiguration(id, value); } catch (OperationCanceledException) { }
            catch (Exception error) { AgentError("Agent 配置未更改", error); }
            finally { applying = false; revision = -1; refresh?.Invoke(); }
        }
        refresh = () => {
            var current = agent; rows.IsEnabled = !applying && current?.Connected == true && !current.Busy && !current.ChangingWorkspace;
            if (applying || ReferenceEquals(rendered, current) && revision == (current?.Configuration.Revision ?? 0)) return;
            rendered = current; revision = current?.Configuration.Revision ?? 0; rows.Children.Clear();
            var options = current?.Configuration.Options ?? [];
            if (options.Length == 0) { rows.Children.Add(Paragraph(current?.Connected == true ? "模型、权限和工作模式由 Harness 配置；此连接尚未提供可调整的选项。" : "连接后显示 Agent 提供的模型、权限和思考选项。")); return; }
            if (!options.Any(o => o.Category == "model")) rows.Children.Add(Paragraph("当前模型：" + current!.CurrentModel));
            foreach (var option in options)
            {
                string label = option.Category switch { "model" => "模型 · ", "mode" => "权限／工作模式 · ", "thought_level" => "思考深度 · ", _ => "" };
                rows.Children.Add(Paragraph(label + option.Name));
                if (option.Description.Length > 0) rows.Children.Add(Paragraph(option.Description));
                if (option.Method.Length == 0) { rows.Children.Add(Paragraph(option.CurrentValue + " · 由 Harness 配置")); continue; }
                if (option.Type == "boolean")
                {
                    var toggle = new CheckBox { Content = option.Name, IsChecked = option.CurrentValue == "true" };
                    toggle.Click += async (_, _) => await Apply(current!, option.Id, toggle.IsChecked == true ? "true" : "false"); rows.Children.Add(toggle);
                }
                else
                {
                    var selector = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
                    var choices = option.Choices.Select(c => { var item = new ComboBoxItem { Content = (c.Group.Length > 0 ? c.Group + " · " : "") + c.Name, Tag = c.Value }; ToolTip.SetTip(item, c.Description); return item; }).ToList();
                    var selected = choices.FirstOrDefault(i => (string?)i.Tag == option.CurrentValue);
                    if (selected == null) { selected = new ComboBoxItem { Content = option.CurrentValue.Length > 0 ? option.CurrentValue : "由 Harness 配置", Tag = option.CurrentValue, IsEnabled = false }; choices.Add(selected); }
                    selector.ItemsSource = choices; selector.SelectedItem = selected;
                    selector.SelectionChanged += async (_, _) => { if (selector.SelectedItem is ComboBoxItem { Tag: string value } && value != option.CurrentValue) await Apply(current!, option.Id, value); };
                    rows.Children.Add(selector);
                }
            }
        }; return refresh;
    }
}
#endif
