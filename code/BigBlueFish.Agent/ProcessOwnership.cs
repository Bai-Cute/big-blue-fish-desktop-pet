using System.Diagnostics;
using System.Runtime.InteropServices;

namespace BigBlueFish.Agent;

// The job closes when the pet terminates, including abrupt process exit. Existing user CLI processes are never attached.
internal sealed class ProcessOwnership : IDisposable
{
    private IntPtr job;
    public ProcessOwnership(Process process)
    {
        if (!OperatingSystem.IsWindows()) return;
        job = CreateJobObject(IntPtr.Zero, null);
        if (job == IntPtr.Zero) return;
        var limits = new ExtendedLimits(); limits.Basic.Flags = 0x2000;
        if (!SetInformationJobObject(job, 9, ref limits, (uint)Marshal.SizeOf<ExtendedLimits>()) || !AssignProcessToJobObject(job, process.Handle)) Dispose();
    }
    public void Dispose() { if (job != IntPtr.Zero) { CloseHandle(job); job = IntPtr.Zero; } }
    [StructLayout(LayoutKind.Sequential)] private struct BasicLimits { public long ProcessTime, JobTime; public uint Flags; public UIntPtr Minimum, Maximum; public uint Active; public UIntPtr Affinity; public uint Priority, Scheduling; }
    [StructLayout(LayoutKind.Sequential)] private struct Io { public ulong ReadOps, WriteOps, OtherOps, ReadBytes, WriteBytes, OtherBytes; }
    [StructLayout(LayoutKind.Sequential)] private struct ExtendedLimits { public BasicLimits Basic; public Io Io; public UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateJobObject(IntPtr attributes, string? name);
    [DllImport("kernel32.dll")] private static extern bool SetInformationJobObject(IntPtr handle, int type, ref ExtendedLimits value, uint length);
    [DllImport("kernel32.dll")] private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
}
