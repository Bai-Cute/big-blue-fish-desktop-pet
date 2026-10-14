using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace BigBlueFish.Windows;

// A no-activate owner cannot reliably capture clicks outside its process.
// Observe button-down only while this menu is open and always pass input through.
internal sealed class CompanionMenuDismissal : IDisposable
{
    private readonly ContextMenu menu;
    private readonly MouseCallback callback;
    private IntPtr hook;
    private long session;
    private bool disposed;
    internal bool IsListening => hook != IntPtr.Zero;

    internal CompanionMenuDismissal(ContextMenu menu)
    {
        this.menu = menu;
        callback = MouseMessage;
        menu.Opened += Opened;
        menu.Closed += Closed;
    }

    private void Opened(object sender, RoutedEventArgs args)
    {
        if (disposed || IsListening) return;
        session++;
        hook = SetWindowsHookEx(14, callback, GetModuleHandle(null), 0); // WH_MOUSE_LL
        if (hook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    private void Closed(object sender, RoutedEventArgs args) => Stop();
    private void Stop()
    {
        session++;
        if (hook == IntPtr.Zero) return;
        UnhookWindowsHookEx(hook);
        hook = IntPtr.Zero;
    }

    private IntPtr MouseMessage(int code, IntPtr message, IntPtr data)
    {
        long kind = message.ToInt64();
        if (code >= 0 && kind is 0x201 or 0x204 or 0x207 or 0x20B && !disposed)
        {
            var position = Marshal.PtrToStructure<MouseData>(data).Position;
            long capturedSession = session;
            // Release menu capture before Windows routes this button-down. Only
            // bounds checks/closing run here; never perform layout, IO or model work.
            if (menu.Dispatcher.CheckAccess())
                DismissOutside(new Point(position.X, position.Y));
            else if (!menu.Dispatcher.HasShutdownStarted)
                menu.Dispatcher.BeginInvoke(DispatcherPriority.Input, (Action)(() =>
                {
                    if (!disposed && capturedSession == session)
                        DismissOutside(new Point(position.X, position.Y));
                }));
        }
        return CallNextHookEx(hook, code, message, data);
    }

    internal void DismissOutside(Point physicalPoint)
    {
        if (!menu.IsOpen || PresentationSource.FromVisual(menu) == null) return;
        var first = menu.PointToScreen(new Point(0, 0));
        var last = menu.PointToScreen(new Point(menu.ActualWidth, menu.ActualHeight));
        if (!new Rect(first, last).Contains(physicalPoint)) menu.IsOpen = false;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        menu.Opened -= Opened;
        menu.Closed -= Closed;
        Stop();
        GC.KeepAlive(callback);
    }

    private delegate IntPtr MouseCallback(int code, IntPtr message, IntPtr data);
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseData
    {
        public NativePoint Position;
        public uint Mouse, Flags, Time;
        public UIntPtr Extra;
    }
    [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int kind, MouseCallback callback, IntPtr module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
}
