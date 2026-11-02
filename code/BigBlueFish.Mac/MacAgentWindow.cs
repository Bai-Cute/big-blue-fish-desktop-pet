using BigBlueFish.Agent;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ShapePath = Avalonia.Controls.Shapes.Path;

namespace BigBlueFish.Windows;

internal sealed partial class MacPetWindow
{
    const double DefaultChatOpacity = .94;
    static readonly Color Ink = Color.FromRgb(20, 47, 82);
    static readonly Color MutedInk = Color.FromRgb(91, 119, 148);
    static readonly Color Aqua = Color.FromRgb(42, 183, 190);
    static readonly Color AquaSoft = Color.FromRgb(220, 247, 246);
    static readonly Color UserSurface = Color.FromRgb(226, 240, 255);
    static readonly Color AssistantSurface = Color.FromRgb(247, 251, 255);
    static readonly Color WindowSurface = Color.FromArgb(246, 250, 253, 255);
    static readonly Color Outline = Color.FromArgb(105, 132, 169, 190);
    const string PinGeometry = "M12 17v5 M9 10.76a2 2 0 0 1-1.11 1.79l-1.78.9A2 2 0 0 0 5 15.24V16a1 1 0 0 0 1 1h12a1 1 0 0 0 1-1v-.76a2 2 0 0 0-1.11-1.79l-1.78-.9A2 2 0 0 1 15 10.76V7a1 1 0 0 1 1-1 2 2 0 0 0 0-4H8a2 2 0 0 0 0 4 1 1 0 0 1 1 1z";
    const string PinOffGeometry = "M12 17v5 M15 9.34V7a1 1 0 0 1 1-1 2 2 0 0 0 0-4H7.89 M2 2L22 22 M9 9v1.76a2 2 0 0 1-1.11 1.79l-1.78.9A2 2 0 0 0 5 15.24V16a1 1 0 0 0 1 1h11";

    Window? chat, permissionWindow;
    readonly MacAgentSession agentSession = new();
    CancellationTokenSource? agentOperation;
    Task? agentDisconnectTask;
    readonly MacChatRequestQueue<ChatRequest> chatQueue = new();
    readonly List<MacChatEntry> chatMessages = new();
    readonly Dictionary<MacChatEntry, (TextBlock Text, Border Card)> chatEntryControls = new();
    readonly MacChatScrollState chatScrollState = new();
    ScrollViewer? chatScroller;
    StackPanel? chatTranscript;
    TextBox? chatInput;
    TextBlock? chatStatus;
    Button? chatSend, chatPin, chatNewMessages;
    bool chatPinned;
    double chatOpacity = DefaultChatOpacity;

    readonly record struct ChatRequest(string Message, MacChatEntry ActivityEntry, int ConversationVersion);
    int chatConversationVersion;

    Task DisconnectAgent()
    {
        agentOperation?.Cancel();
        while (chatQueue.TryDequeue(out var pending))
            if (pending.ConversationVersion == chatConversationVersion)
                UpdateChatEntry(pending.ActivityEntry, "已取消", isActivity: false, countsAsNewMessage: true);
        permissionWindow?.Close();
        agentDisconnectTask = DisposeAgentAfter(agentDisconnectTask);
        return agentDisconnectTask;
    }

    async Task DisposeAgentAfter(Task? previous)
    {
        if (previous != null) {
            try { await previous; } catch { }
        }
        await agentSession.DisposeAsync();
    }

    internal void CloseChat() => chat?.Close();

    internal void ApplyChatOpacity(double opacity)
    {
        chatOpacity = Math.Clamp(double.IsFinite(opacity) ? opacity : DefaultChatOpacity, .55, 1);
        if (chat != null) chat.Opacity = chatOpacity;
    }

    internal void SetChatStatus(string status)
    {
        if (chatStatus != null) chatStatus.Text = status;
    }

    async Task<string?> RequestPermission(AgentPermission request, CancellationToken token)
    {
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        await Dispatcher.UIThread.InvokeAsync(() => {
            if (token.IsCancellationRequested || exiting || permissionWindow != null) { completion.TrySetResult(null); return; }
            var body = new StackPanel { Spacing = 12, Margin = new Thickness(24) };
            body.Children.Add(new TextBlock { Text = request.Title, TextWrapping = TextWrapping.Wrap, FontSize = 21, FontWeight = FontWeight.SemiBold, Foreground = Brush(Ink) });
            body.Children.Add(new TextBlock { Text = request.Detail[..Math.Min(request.Detail.Length, 8000)], TextWrapping = TextWrapping.Wrap, Foreground = Brush(MutedInk), MaxWidth = 560 });
            var actions = new StackPanel { Spacing = 8, Margin = new Thickness(0, 10, 0, 0) };
            foreach (var option in request.Options.Where(o => o.Kind != "allow_always")) {
                var button = ActionButton(option.Name, () => { completion.TrySetResult(option.Id); permissionWindow?.Close(); }, option.Kind is "allow_once" or "approve");
                actions.Children.Add(button);
            }
            actions.Children.Add(ActionButton("拒绝 / 取消", () => permissionWindow?.Close(), false));
            body.Children.Add(actions);
            permissionWindow = new Window {
                Title = "Agent 操作批准", Width = 620, SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = Brushes.Transparent, TransparencyLevelHint = [WindowTransparencyLevel.Transparent],
                Content = Surface(body, 18, WindowSurface)
            };
            permissionWindow.Closed += (_, _) => { permissionWindow = null; completion.TrySetResult(null); };
            permissionWindow.Show(); permissionWindow.Activate();
        });
        using var cancelled = token.Register(() => { completion.TrySetResult(null); Dispatcher.UIThread.Post(() => permissionWindow?.Close()); });
        return await completion.Task;
    }

    void OpenAgent()
    {
        if (exiting) return;
        if (chat != null) { chat.Show(); chat.Activate(); ScrollChatToEnd(); return; }

        chatStatus = new TextBlock { Text = BackendStatus(), FontSize = 12, Foreground = Brush(MutedInk), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(22, 18, 18, 12), Background = Brushes.Transparent };
        var brand = new StackPanel { Spacing = 2 };
        brand.Children.Add(new TextBlock { Text = "和大肥鱼聊天", FontSize = 18, FontWeight = FontWeight.Medium, Foreground = Brush(Ink) });
        brand.Children.Add(chatStatus);
        header.Children.Add(brand);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        var clear = HeaderIconButton("清空聊天记录", "M4 7h16 M10 11v6 M14 11v6 M5 7l1 14h12l1-14z M9 7V4h6v3", ClearConversation, destructive: true);
        chatPin = HeaderIconButton(chatPinned ? "取消固定对话窗" : "固定对话窗", chatPinned ? PinGeometry : PinOffGeometry,
            ToggleChatPin, selected: () => chatPinned, selectedGeometry: PinGeometry, selectedOffsetX: 1.75);
        var pin = chatPin;
        var close = HeaderIconButton("关闭对话窗", "M18 6 6 18 M6 6l12 12", CloseChat);
        actions.Children.Add(clear); actions.Children.Add(pin); actions.Children.Add(close);
        Grid.SetColumn(actions, 1); header.Children.Add(actions);
        header.AddHandler(InputElement.PointerPressedEvent, (_, e) => {
            if (!e.GetCurrentPoint(header).Properties.IsLeftButtonPressed || clear.IsPointerOver || pin.IsPointerOver || close.IsPointerOver || chat == null) return;
            chat.BeginMoveDrag(e);
            e.Handled = true;
        }, RoutingStrategies.Tunnel);

        chatTranscript = new StackPanel { Spacing = 12, Margin = new Thickness(22, 8, 22, 12) };
        chatScroller = new ScrollViewer {
            Content = chatTranscript,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(0, 2, 2, 0)
        };
        chatScroller.ScrollChanged += (_, _) => {
            if (!IsChatAtBottom()) return;
            chatScrollState.MarkLatestVisible();
            UpdateNewMessageButton();
        };
        chatNewMessages = ActionButton("有新消息 · 查看最新", JumpToLatestMessages, true);
        chatNewMessages.HorizontalAlignment = HorizontalAlignment.Center;
        chatNewMessages.VerticalAlignment = VerticalAlignment.Bottom;
        chatNewMessages.Margin = new Thickness(8, 8, 8, 14);
        chatNewMessages.IsVisible = false;
        var transcriptHost = new Grid();
        transcriptHost.Children.Add(chatScroller);
        transcriptHost.Children.Add(chatNewMessages);

        chatInput = new TextBox {
            AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxLength = 4000, MinHeight = 50, FontSize = 14,
            PlaceholderText = "和鱼鱼说点什么…（回车发送，Shift + 回车换行）", Foreground = Brush(Ink), Background = Brush(Color.FromArgb(210, 255, 255, 255)),
            BorderBrush = Brush(Outline), BorderThickness = new Thickness(1), Padding = new Thickness(14, 10),
            VerticalContentAlignment = VerticalAlignment.Center
        };
        chatInput.AddHandler(InputElement.KeyDownEvent, (_, e) => {
            if (e.Key != Key.Enter || (e.KeyModifiers & KeyModifiers.Shift) != 0) return;
            e.Handled = true;
            _ = SendChat();
        }, RoutingStrategies.Tunnel);
        chatInput.TextChanged += (_, _) => UpdateChatControls();
        chatSend = ActionButton("发送", () => _ = SendChat(), true);
        var composer = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10, Margin = new Thickness(22, 0, 22, 20) };
        composer.Children.Add(chatInput); Grid.SetColumn(chatSend, 1); composer.Children.Add(chatSend);

        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), MinWidth = 420, MinHeight = 520 };
        layout.Children.Add(header);
        Grid.SetRow(transcriptHost, 1); layout.Children.Add(transcriptHost);
        Grid.SetRow(composer, 2); layout.Children.Add(composer);
        chat = new Window {
            Title = "蓝色大肥鱼 · 对话", Width = 560, Height = 680, MinWidth = 420, MinHeight = 480,
            WindowStartupLocation = WindowStartupLocation.CenterScreen, ShowActivated = true,
            WindowDecorations = WindowDecorations.None, CanResize = true, Topmost = true,
            FontFamily = new FontFamily("PingFang SC"), FontSize = 14,
            Background = Brushes.Transparent, TransparencyLevelHint = [WindowTransparencyLevel.Transparent],
            Content = Surface(layout, 20, WindowSurface), Opacity = ChatWindowOpacity
        };
        var chatWindow = chat;
        chatWindow.Deactivated += (_, _) => { if (permissionWindow == null && !chatPinned) chatWindow.Hide(); };
        chatWindow.Closed += async (_, _) => {
            chatConversationVersion++;
            foreach (var entry in chatMessages.Where(entry => entry.IsActivity)) entry.SetResponse("已取消");
            chatEntryControls.Clear();
            if (ReferenceEquals(chat, chatWindow)) chat = null;
            chatScroller = null; chatTranscript = null; chatInput = null; chatStatus = null; chatSend = null; chatPin = null; chatNewMessages = null;
            await DisconnectAgent();
        };
        RenderChat(); UpdateChatControls(); chatWindow.Show(); chatWindow.Activate();
    }

    string BackendStatus()
    {
        return chatSettings.Backend switch {
            "acp" => agentSession.Connected ? "ACP · 已连接" : AcpConfigurationIssue() ?? "ACP · 已配置",
            "gguf" => "本机 GGUF · 待机",
            _ => $"{(chatSettings.ApiProtocol == "responses" ? "Responses" : "Chat Completions")} · {(string.IsNullOrWhiteSpace(chatSettings.Model) ? "未设置模型" : chatSettings.Model)}"
        };
    }

    string? AcpConfigurationIssue()
    {
        if (!chatSettings.AcpConsent) return "ACP · 请在设置中确认权限";
        var path = MacAcpExecutableLocator.Resolve(chatSettings.AcpProvider, chatSettings.AcpExecutable);
        if (chatSettings.AcpProvider == "antigravity" && path.Length == 0) return null;
        if (!Path.IsPathRooted(path)) return "ACP · 请在设置中填写可执行文件绝对路径";
        if (!File.Exists(path)) return "ACP · 找不到可执行文件，请检查设置中的路径";
        return null;
    }

    async Task<bool> EnsureAgentConnected(CancellationToken token)
    {
        if (agentSession.Connected) return true;
        if (AcpConfigurationIssue() is { } issue) { SetChatStatus(issue); return false; }
        var path = MacAcpExecutableLocator.Resolve(chatSettings.AcpProvider, chatSettings.AcpExecutable);
        var workspace = Path.Combine(Environment.CurrentDirectory, "agent/workspace"); Directory.CreateDirectory(workspace);
        var harness = HarnessCatalog.Get(chatSettings.AcpProvider);
        var adapters = new AgentAdapterStore(Path.Combine(Environment.CurrentDirectory, "agent"));
        var command = MacAcpExecutableLocator.FindReadyCommand(harness.Id, path)
            ?? await adapters.Resolve(harness, path, token, SetChatStatus, MacAcpExecutableLocator.FindCommand("npm"));
        var start = AgentProcess.StartInfo(command.Path, command.Arguments, workspace);
        var underlyingCli = harness.AdapterCommand != null
            ? harness.Commands.Select(name => MacAcpExecutableLocator.FindCommand(name)).FirstOrDefault(found => found != null)
            : null;
        start.Environment["PATH"] = Path.GetDirectoryName(command.Path) + Path.PathSeparator +
            (path.Length > 0 ? Path.GetDirectoryName(path) + Path.PathSeparator : "") +
            (underlyingCli != null ? Path.GetDirectoryName(underlyingCli) + Path.PathSeparator : "") +
            (MacAcpExecutableLocator.FindCommand("npm") is { } npm ? Path.GetDirectoryName(npm) + Path.PathSeparator : "") +
            "/opt/homebrew/bin:/usr/local/bin:/usr/bin:/bin:" + Environment.GetEnvironmentVariable("PATH");
        if (harness.Id == "codex" && path.Length > 0 && !MacAcpExecutableLocator.IsAdapterExecutable(harness.Id, path))
            start.Environment["CODEX_PATH"] = path;
        if (harness.Id == "antigravity" && AgentAdapterStore.AntigravityHelper(command.Path) is { } helper)
            start.Environment["ANTIGRAVITY_HARNESS_PATH"] = helper;
        SetChatStatus("ACP · 正在连接…");
        await agentSession.Connect(start, workspace, RequestPermission, token);
        UpdateChatActivityStatus();
        return true;
    }

    async Task SendChat()
    {
        if (chatInput == null) return;
        var message = chatInput.Text?.Trim() ?? "";
        if (message.Length == 0) return;
        chatInput.Text = "";
        AppendChatMessage(new MacChatEntry(true, message), forceFollow: true);
        var activityEntry = new MacChatEntry(false, chatQueue.IsProcessing ? "排队中…" : "正在思考…", isActivity: true);
        AppendChatMessage(activityEntry, forceFollow: true);
        chatQueue.Enqueue(new ChatRequest(message, activityEntry, chatConversationVersion));
        UpdateChatControls();
        UpdateChatActivityStatus();
        if (!chatQueue.TryStartProcessing()) return;

        try {
            await ProcessChatQueue();
        } catch (Exception error) {
            chatQueue.StopProcessing();
            UpdateChatControls();
            SetChatStatus("请求未完成：" + error.Message);
        }
    }

    async Task ProcessChatQueue()
    {
        while (true) {
            string? finalStatus = null;
            try {
                while (chatQueue.TryDequeue(out var request)) {
                    finalStatus = null;
                    agentOperation?.Dispose();
                    var operation = new CancellationTokenSource();
                    agentOperation = operation;
                    var token = operation.Token;
                    if (request.ConversationVersion == chatConversationVersion)
                        UpdateChatEntry(request.ActivityEntry, "正在思考…", isActivity: true);
                    UpdateChatActivityStatus();
                    bool cancelled = false;
                    try {
                        string response;
                        if (chatSettings.Backend == "acp") {
                            if (agentDisconnectTask is { } disconnect) await disconnect.WaitAsync(token);
                            if (!await EnsureAgentConnected(token)) {
                                if (request.ConversationVersion == chatConversationVersion) {
                                    var issue = chatStatus?.Text ?? "ACP 未连接";
                                    UpdateChatEntry(request.ActivityEntry, issue, isActivity: false, countsAsNewMessage: true);
                                    finalStatus = issue;
                                }
                                continue;
                            }
                            response = await agentSession.Send(request.Message, token, chatSettings.PersonalityPrompt);
                        } else response = await localChat.Send(request.Message, chatSettings, apiKey, token);
                        if (request.ConversationVersion == chatConversationVersion) {
                            UpdateChatEntry(request.ActivityEntry, response, isActivity: false, countsAsNewMessage: true);
                            if (response.Length > 0) SayChatReply(response);
                        }
                    } catch (OperationCanceledException) {
                        cancelled = true;
                        if (request.ConversationVersion == chatConversationVersion) {
                            UpdateChatEntry(request.ActivityEntry, "已取消", isActivity: false, countsAsNewMessage: true);
                            finalStatus = "已取消";
                        }
                    } catch (Exception error) {
                        if (request.ConversationVersion == chatConversationVersion) {
                            finalStatus = "请求未完成：" + error.Message;
                            UpdateChatEntry(request.ActivityEntry, finalStatus, isActivity: false, countsAsNewMessage: true);
                        }
                    } finally {
                        if (ReferenceEquals(agentOperation, operation)) agentOperation = null;
                        operation.Dispose();
                    }

                    if (cancelled) break;
                }
            } finally {
                chatQueue.StopProcessing();
                UpdateChatControls();
            }

            if (!chatQueue.TryStartProcessing()) {
                SetChatStatus(finalStatus ?? BackendStatus());
                return;
            }
        }
    }

    void UpdateChatActivityStatus()
    {
        if (!chatQueue.IsProcessing) {
            SetChatStatus(BackendStatus());
            return;
        }

        int pending = chatQueue.PendingCount;
        SetChatStatus(pending == 0 ? "正在思考…" : $"正在思考… · {pending} 条排队中");
    }

    async void ClearConversation()
    {
        chatConversationVersion++;
        agentOperation?.Cancel(); chatQueue.Clear(); localChat.Reset(); chatMessages.Clear(); chatEntryControls.Clear(); RenderChat(); SetChatStatus(BackendStatus());
        if (chatSettings.Backend == "acp") await DisconnectAgent();
    }

    void RenderChat()
    {
        if (chatTranscript == null) return;
        chatScrollState.Reset();
        chatEntryControls.Clear();
        chatTranscript.Children.Clear();
        if (chatMessages.Count == 0) {
            chatTranscript.Children.Add(new Border { Background = Brush(AquaSoft), CornerRadius = new CornerRadius(16), Padding = new Thickness(16), Child = new TextBlock { Text = "今天想和鱼鱼聊点什么？", TextWrapping = TextWrapping.Wrap, Foreground = Brush(Ink), FontSize = 14, LineHeight = 21 } });
            UpdateNewMessageButton();
            ScrollChatToEnd();
            return;
        }
        foreach (var message in chatMessages) AddMessage(message);
        UpdateNewMessageButton();
        ScrollChatToEnd();
    }

    void AppendChatMessage(MacChatEntry entry, bool forceFollow = false)
    {
        bool wasAtBottom = IsChatAtBottom();
        bool follow = chatScrollState.OnMessageAdded(wasAtBottom, forceFollow);
        chatMessages.Add(entry);
        if (chatTranscript != null) {
            if (chatMessages.Count == 1) chatTranscript.Children.Clear();
            AddMessage(entry);
        }
        UpdateNewMessageButton();
        if (follow) ScrollChatToEnd();
    }

    void AddMessage(MacChatEntry entry)
    {
        if (chatTranscript == null) return;
        var label = new TextBlock { Text = entry.IsUser ? "你" : "鱼鱼", FontSize = 12, Foreground = Brush(MutedInk), Margin = new Thickness(4, 0, 4, 4) };
        var content = new TextBlock { Text = entry.Text, TextWrapping = TextWrapping.Wrap, Foreground = Brush(entry.IsActivity ? MutedInk : Ink), FontSize = 14, LineHeight = 21 };
        var card = new Border { Background = Brush(entry.IsUser ? UserSurface : entry.IsActivity ? AquaSoft : AssistantSurface), BorderBrush = Brush(entry.IsUser ? Color.FromRgb(190, 218, 247) : Outline), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(15), Padding = new Thickness(14, 11), MaxWidth = 430, Child = content };
        chatEntryControls[entry] = (content, card);
        var group = new StackPanel { Spacing = 0, HorizontalAlignment = entry.IsUser ? HorizontalAlignment.Right : HorizontalAlignment.Left };
        group.Children.Add(label); group.Children.Add(card); chatTranscript.Children.Add(group);
    }

    void UpdateChatEntry(MacChatEntry entry, string text, bool isActivity, bool countsAsNewMessage = false)
    {
        bool wasAtBottom = IsChatAtBottom();
        if (isActivity) entry.SetStatus(text); else entry.SetResponse(text);
        if (chatEntryControls.TryGetValue(entry, out var controls)) {
            controls.Text.Text = entry.Text;
            controls.Text.Foreground = Brush(entry.IsActivity ? MutedInk : Ink);
            controls.Card.Background = Brush(entry.IsUser ? UserSurface : entry.IsActivity ? AquaSoft : AssistantSurface);
        }

        if (countsAsNewMessage) {
            if (chatScrollState.OnMessageAdded(wasAtBottom)) ScrollChatToEnd();
        } else if (wasAtBottom) ScrollChatToEnd();
        UpdateNewMessageButton();
    }

    bool IsChatAtBottom() => chatScroller == null || MacChatScrollState.IsAtBottom(
        chatScroller.Extent.Height, chatScroller.Viewport.Height, chatScroller.Offset.Y);

    void ScrollChatToEnd()
    {
        chatScrollState.MarkLatestVisible();
        UpdateNewMessageButton();
        Dispatcher.UIThread.Post(() => chatScroller?.ScrollToEnd(), DispatcherPriority.Background);
    }

    void JumpToLatestMessages() => ScrollChatToEnd();

    void UpdateNewMessageButton()
    {
        if (chatNewMessages == null) return;
        int unread = chatScrollState.UnreadCount;
        chatNewMessages.Content = unread == 1 ? "有新消息 · 查看最新" : $"有 {unread} 条新消息 · 查看最新";
        chatNewMessages.IsVisible = unread > 0;
    }

    void ToggleChatPin()
    {
        chatPinned = !chatPinned;
        if (chatPin == null) return;
        var label = chatPinned ? "取消固定对话窗" : "固定对话窗";
        ToolTip.SetTip(chatPin, label);
        AutomationProperties.SetName(chatPin, label);
    }

    void UpdateChatControls()
    {
        if (chatSend != null) chatSend.IsEnabled = chatInput?.Text?.Trim().Length > 0;
        if (chatInput != null) chatInput.IsEnabled = true;
    }

    static IBrush Brush(Color color) => new SolidColorBrush(color);
    static Button HeaderIconButton(string label, string geometry, Action action, bool destructive = false, Func<bool>? selected = null, string? selectedGeometry = null, double selectedOffsetX = 0)
    {
        var glyph = new ShapePath {
            Data = Geometry.Parse(geometry), Width = 16, Height = 16, Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Fill = Brushes.Transparent, Stroke = Brush(MutedInk), StrokeThickness = 1.7,
            StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round
        };
        var surface = new Border {
            Width = 34, Height = 34, CornerRadius = new CornerRadius(9),
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent, BorderThickness = new Thickness(1), Child = glyph
        };
        var button = new Button {
            Content = surface, Width = 36, Height = 36, Padding = new Thickness(1),
            Background = Brushes.Transparent, BorderBrush = Brushes.Transparent, BorderThickness = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
            Template = new FuncControlTemplate<Button>((control, _) => new ContentPresenter {
                Content = control.Content, Padding = control.Padding,
                HorizontalContentAlignment = control.HorizontalContentAlignment,
                VerticalContentAlignment = control.VerticalContentAlignment,
                HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch
            }),
            FocusAdorner = new FuncTemplate<Control>(() => new Border {
                Background = Brushes.Transparent, BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(0), IsHitTestVisible = false
            })
        };
        ToolTip.SetTip(button, label);
        AutomationProperties.SetName(button, label);
        var currentGeometry = geometry;
        void Refresh(bool hovered)
        {
            bool active = selected?.Invoke() == true;
            var targetGeometry = active && selectedGeometry != null ? selectedGeometry : geometry;
            if (currentGeometry != targetGeometry) { glyph.Data = Geometry.Parse(targetGeometry); currentGeometry = targetGeometry; }
            // The selected pin reads left-biased at macOS display scale despite centered path bounds.
            glyph.RenderTransform = active && selectedOffsetX != 0 ? new TranslateTransform(selectedOffsetX, 0) : null;
            surface.Background = Brush(active ? AquaSoft : hovered
                ? destructive ? Color.FromRgb(255, 237, 239) : AquaSoft
                : Colors.Transparent);
            surface.BorderBrush = Brush(active ? Aqua : hovered
                ? destructive ? Color.FromRgb(232, 184, 190) : Color.FromArgb(105, Aqua.R, Aqua.G, Aqua.B)
                : Colors.Transparent);
            glyph.Stroke = Brush(destructive && hovered ? Color.FromRgb(181, 67, 78) : active ? Aqua : MutedInk);
        }
        button.PointerEntered += (_, _) => Refresh(true);
        button.PointerExited += (_, _) => Refresh(false);
        button.GotFocus += (_, _) => Refresh(false);
        button.LostFocus += (_, _) => Refresh(false);
        button.Click += (_, _) => { action(); Refresh(false); };
        Refresh(false);
        return button;
    }
    static Border Surface(Control child, double radius, Color color) => new() { Background = Brush(color), BorderBrush = Brush(Outline), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(radius), Padding = new Thickness(1), Child = child };
    static Button ActionButton(string label, Action action, bool primary)
    {
        var button = new Button { Content = label, Padding = new Thickness(15, 8), Margin = new Thickness(0, 0, 6, 0), Background = Brush(primary ? Aqua : Color.FromArgb(35, 255, 255, 255)), Foreground = Brush(primary ? Colors.White : Ink), BorderBrush = Brush(primary ? Aqua : Outline), BorderThickness = new Thickness(1), HorizontalContentAlignment = HorizontalAlignment.Center };
        button.Click += (_, _) => action(); return button;
    }
}
