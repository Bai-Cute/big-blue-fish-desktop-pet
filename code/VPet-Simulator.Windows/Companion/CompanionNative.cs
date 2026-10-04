using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;

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
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetClassName(IntPtr h, StringBuilder s, int n);
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
        if (h == IntPtr.Zero)
            return "主人正在使用 Windows 桌面";
        GetWindowThreadProcessId(h, out var id);
        if (id == Environment.ProcessId)
            return "用户正在查看桌宠";
        var s = new StringBuilder(256);
        GetWindowText(h, s, 256);
        var c = new StringBuilder(128);
        GetClassName(h, c, c.Capacity);
        string name = "应用";
        try
        {
            name = Process.GetProcessById((int)id).ProcessName;
        }
        catch
        {
        }

        string accessibleName = "";
        string controlType = "";
        try
        {
            var element = AutomationElement.FromHandle(h);
            accessibleName = element.Current.Name ?? "";
            controlType = element.Current.ControlType?.ProgrammaticName ?? "";
        }
        catch
        {
            // Some elevated, secure, or transient windows do not expose UI Automation.
        }

        return CompanionScreenContext.Summarize(name, s.ToString(), c.ToString(), accessibleName, controlType);
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

internal static class CompanionScreenContext
{
    internal static string Summarize(string processName, string title, string className, string accessibleName, string controlType)
    {
        string process = (processName ?? "").Trim().ToLowerInvariant();
        string windowClass = (className ?? "").Trim().ToLowerInvariant();
        string combined = string.Join(" ", title ?? "", accessibleName ?? "").ToLowerInvariant();

        if (windowClass is "progman" or "workerw" or "shell_traywnd" ||
            (process is "explorer" or "explorer.exe" &&
             (combined.Length == 0 || combined.Contains("桌面") || combined.Contains("desktop"))))
            return "主人正在使用 Windows 桌面";

        if (process is "explorer" or "explorer.exe")
            return "主人在查看文件";

        if (process is "devenv" or "code" or "code-insiders" or "codium" or "rider64" or "idea64" or "clion64" or "pycharm64" or "notepad++")
        {
            return combined.Contains("蓝色大肥鱼") || combined.Contains("big blue fish") || combined.Contains("bigbluefish")
                ? "主人在改蓝色大肥鱼的代码"
                : "主人在编辑代码";
        }

        if (process is "powershell" or "pwsh" or "cmd" or "windowsterminal" or "wt")
            return "主人在使用命令行处理工作";

        if (process is "winword" or "excel" or "powerpnt" or "soffice" or "wps" or "wpscloudsvr")
            return "主人在编辑办公文档";

        if (process is "chrome" or "msedge" or "firefox" or "brave" or "opera" or "vivaldi")
            return "主人正在浏览网页";

        if (process is "vlc" or "potplayer" or "mpc-hc" or "mpc-be")
            return "主人正在观看视频或媒体";

        if (combined.Contains("蓝色大肥鱼") || combined.Contains("big blue fish") || combined.Contains("bigbluefish"))
            return "主人在改蓝色大肥鱼的代码";

        if (controlType.Contains("document", StringComparison.OrdinalIgnoreCase))
            return "主人正在查看或编辑文档";

        return "主人正在使用电脑上的应用";
    }
}
