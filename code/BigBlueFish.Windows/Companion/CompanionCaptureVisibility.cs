using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using BigBlueFish.Diagnostics;

namespace BigBlueFish.Windows;

internal static class CompanionCaptureVisibility
{
    sealed record Entry(WeakReference<Window> Window, Func<bool> Enabled);
    static readonly List<Entry> windows = new();
    internal static bool Supported => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041);

    internal static void Track(Window window, Func<bool> enabled)
    {
        windows.Add(new(new(window), enabled));
        window.SourceInitialized += (_, _) => Apply(window, enabled());
        window.IsVisibleChanged += (_, _) => Apply(window, enabled());
        window.Closed += (_, _) => windows.RemoveAll(x => !x.Window.TryGetTarget(out var w) || ReferenceEquals(w, window));
        Apply(window, enabled());
    }

    internal static void Refresh()
    {
        windows.RemoveAll(x => !x.Window.TryGetTarget(out _));
        foreach (var entry in windows)
            if (entry.Window.TryGetTarget(out var window)) Apply(window, entry.Enabled());
    }

    internal static bool Apply(Window window, bool enabled)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return false;
        if (!Supported) return false;
        uint desired = enabled ? 0x11u : 0;
        if (GetWindowDisplayAffinity(handle, out uint current) && current == desired) return true;
        bool success = SetWindowDisplayAffinity(handle, desired);
        int error = success ? 0 : Marshal.GetLastWin32Error();
        DiagnosticLog.Write(DiagnosticEvent.CaptureVisibility, enabled: enabled, state: success ? "success" : "failed",
            error: success ? null : new Win32Exception(error));
        return success;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetWindowDisplayAffinity(IntPtr hwnd, out uint affinity);
}
