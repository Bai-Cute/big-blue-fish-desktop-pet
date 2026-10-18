using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace BigBlueFish.Windows;

// Observe Shell registration itself: a requested Visible=true is not evidence
// that Explorer accepted the icon. Keep this window independent of the pet HWND.
internal sealed class CompanionTrayIcon : IDisposable
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct IconData
    {
        internal uint Size; internal IntPtr Window; internal uint Id, Flags, Callback;
        internal IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string Tip;
        internal uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] internal string Info;
        internal uint Timeout;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] internal string Title;
        internal uint InfoFlags; internal Guid Guid; internal IntPtr BalloonIcon;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct Identifier { internal uint Size; internal IntPtr Window; internal uint Id; internal Guid Guid; }
    [StructLayout(LayoutKind.Sequential)] struct Rect { internal int Left, Top, Right, Bottom; }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool Shell_NotifyIcon(uint operation, ref IconData data);
    [DllImport("shell32.dll")] static extern int Shell_NotifyIconGetRect(ref Identifier identifier, out Rect rectangle);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr window, uint message, IntPtr w, IntPtr l);
    const uint CallbackMessage = 0x8001;
    readonly uint taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    readonly Guid identity = new(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(AppContext.BaseDirectory).ToUpperInvariant()))[..16]);
    readonly TrayWindow window;
    readonly DispatcherTimer retry = new() { Interval = TimeSpan.FromSeconds(3) };
    readonly Func<uint, IconData, bool>? shellOverride;
    bool visible, disposed, failureReported;
    internal Icon? Icon { get; set; }
    internal string Text { get; set; } = "";
    internal Forms.ContextMenuStrip? ContextMenuStrip { get; set; }
    internal bool Registered { get; private set; }
    internal IntPtr Handle => window.Handle;
    internal event EventHandler? DoubleClick;
    internal event EventHandler? BalloonTipClicked;
    internal bool Visible
    {
        get => visible;
        set
        {
            if (disposed || value == visible) return;
            visible = value;
            if (value) { EnsureRegistered(); retry.Start(); }
            else { retry.Stop(); var data = Data(); Send(2, ref data); Registered = false; }
        }
    }
    internal CompanionTrayIcon(Func<uint, IconData, bool>? shellOverride = null)
    {
        this.shellOverride = shellOverride;
        window = new TrayWindow(this);
        window.CreateHandle(new Forms.CreateParams { Caption = "BigBlueFish.Tray" });
        retry.Tick += (_, _) => EnsureRegistered();
    }
    IconData Data() => new() { Size = (uint)Marshal.SizeOf<IconData>(), Window = window.Handle, Id = 1,
        Flags = 1 | 2 | 4 | 0x20, Callback = CallbackMessage, Icon = Icon?.Handle ?? IntPtr.Zero,
        Tip = Text, Info = "", Title = "", Guid = identity };
    bool Send(uint operation, ref IconData data) => shellOverride?.Invoke(operation, data) ?? Shell_NotifyIcon(operation, ref data);
    internal bool IsPresent()
    {
        var id = new Identifier { Size = (uint)Marshal.SizeOf<Identifier>(), Window = window.Handle, Id = 1, Guid = identity };
        return Shell_NotifyIconGetRect(ref id, out _) == 0;
    }
    internal void EnsureRegistered()
    {
        if (!visible || disposed || Icon == null) return;
        if (Registered && IsPresent()) return;
        var data = Data();
        Registered = Send(0, ref data);
        if (!Registered) Registered = Send(1, ref data);
        if (Registered) { data.Timeout = 3; Send(4, ref data); }
        if (!Registered && !failureReported)
        {
            failureReported = true;
            Record("Windows 未接受托盘图标，将在后台重试。Win32=" + Marshal.GetLastWin32Error());
        }
        else if (Registered && failureReported) { failureReported = false; Record("托盘图标已恢复。"); }
        retry.Interval = TimeSpan.FromSeconds(Registered ? 30 : 3);
    }
    static void Record(string message)
    {
        try { File.AppendAllText("companion-errors.log", DateTime.Now.ToString("s") + " " + message + Environment.NewLine); } catch { }
    }
    internal void ShowBalloonTip(int timeout, string title, string text, Forms.ToolTipIcon icon)
    {
        EnsureRegistered(); if (!Registered) return;
        var data = Data(); data.Flags |= 0x10; data.Info = text; data.Title = title;
        data.Timeout = (uint)timeout; data.InfoFlags = (uint)icon;
        Send(1, ref data);
    }
    void Message(ref Forms.Message message)
    {
        if ((uint)message.Msg == taskbarCreated) { Registered = false; EnsureRegistered(); return; }
        if ((uint)message.Msg != CallbackMessage) return;
        uint action = unchecked((uint)message.LParam.ToInt64());
        if (action is 0x202 or 0x203 or 0x400 or 0x401) DoubleClick?.Invoke(this, EventArgs.Empty);
        else if (action == 0x405) BalloonTipClicked?.Invoke(this, EventArgs.Empty);
        else if (action is 0x205 or 0x7B && ContextMenuStrip != null)
        {
            SetForegroundWindow(window.Handle);
            ContextMenuStrip.Show(Forms.Cursor.Position);
            PostMessage(window.Handle, 0, IntPtr.Zero, IntPtr.Zero);
        }
    }
    public void Dispose()
    {
        if (disposed) return;
        Visible = false; disposed = true; retry.Stop(); window.DestroyHandle(); Icon?.Dispose(); ContextMenuStrip?.Dispose();
    }
    sealed class TrayWindow(CompanionTrayIcon owner) : Forms.NativeWindow
    {
        protected override void WndProc(ref Forms.Message message) { owner.Message(ref message); base.WndProc(ref message); }
    }
}
