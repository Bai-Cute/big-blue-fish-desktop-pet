using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace VPet_Simulator.Windows;
internal static class CompanionBuild
{
    public static readonly bool Enabled = true;
}

internal static class CompanionNative
{
    internal static bool? TestPowerOverride;
    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    internal static extern bool GetWindowRect(IntPtr h, out Rect r);
    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(IntPtr h, out uint p);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")]
    internal static extern bool GetCursorPos(out Point p);
    [DllImport("user32.dll")]
    internal static extern bool GetLastInputInfo(ref Input i);
    [DllImport("kernel32.dll")]
    internal static extern bool GetSystemPowerStatus(out Power p);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    internal static extern IntPtr GetStyle(IntPtr h, int n);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    internal static extern IntPtr SetStyle(IntPtr h, int n, IntPtr v);
    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Point
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Input
    {
        public uint Size, Time;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Power
    {
        public byte AC, Battery, Percent, Reserved;
        public uint Life, FullLife;
    }

    internal static bool OnAC => TestPowerOverride ?? (GetSystemPowerStatus(out var p) && p.AC == 1);

    internal static double IdleSeconds
    {
        get
        {
            var i = new Input
            {
                Size = (uint)Marshal.SizeOf<Input>()
            };
            return GetLastInputInfo(ref i) ? unchecked((uint)Environment.TickCount - i.Time) / 1000.0 : 0;
        }
    }

    internal static string ForegroundContext()
    {
        var h = GetForegroundWindow();
        GetWindowThreadProcessId(h, out var id);
        if (id == Environment.ProcessId)
            return "用户正在查看桌宠";
        var s = new StringBuilder(256);
        GetWindowText(h, s, 256);
        string name = "应用";
        try
        {
            name = Process.GetProcessById((int)id).ProcessName;
        }
        catch
        {
        }

        return name + "；窗口标题：" + s.ToString();
    }

    internal static bool IsFullscreen()
    {
        var h = GetForegroundWindow();
        GetWindowThreadProcessId(h, out var id);
        if (id == Environment.ProcessId || !GetWindowRect(h, out var r))
            return false;
        try
        {
            var n = Process.GetProcessById((int)id).ProcessName;
            if (n == "explorer")
                return false;
        }
        catch
        {
        }

        var b = System.Windows.Forms.Screen.FromHandle(h).Bounds;
        return r.Left <= b.Left && r.Top <= b.Top && r.Right >= b.Right && r.Bottom >= b.Bottom;
    }
}
