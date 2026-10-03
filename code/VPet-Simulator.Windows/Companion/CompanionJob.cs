using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace VPet_Simulator.Windows;
internal sealed class CompanionJob : IDisposable
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr CreateJobObject(IntPtr a, string? n);
    [DllImport("kernel32.dll")]
    static extern bool SetInformationJobObject(IntPtr h, int c, IntPtr p, uint n);
    [DllImport("kernel32.dll")]
    static extern bool AssignProcessToJobObject(IntPtr j, IntPtr p);
    [DllImport("kernel32.dll")]
    static extern bool CloseHandle(IntPtr h);
    private IntPtr handle;
    internal CompanionJob()
    {
        handle = CreateJobObject(IntPtr.Zero, null);
        // JOBOBJECT_EXTENDED_LIMIT_INFORMATION x64: BasicLimitInformation.LimitFlags at byte 16.
        var data = Marshal.AllocHGlobal(144);
        try
        {
            for (int i = 0; i < 144; i++)
                Marshal.WriteByte(data, i, 0);
            Marshal.WriteInt32(data, 16, 0x2000);
            if (handle == IntPtr.Zero || !SetInformationJobObject(handle, 9, data, 144))
                throw new InvalidOperationException("无法初始化进程退出保障");
        }
        finally
        {
            Marshal.FreeHGlobal(data);
        }
    }

    internal bool Attach(Process p) => AssignProcessToJobObject(handle, p.Handle);
    public void Dispose()
    {
        if (handle != IntPtr.Zero)
        {
            CloseHandle(handle);
            handle = IntPtr.Zero;
        }
    }
}
