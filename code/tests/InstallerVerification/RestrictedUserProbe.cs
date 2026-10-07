using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace BigBlueFish.InstallerVerification;

internal static class RestrictedUserProbe
{
    [StructLayout(LayoutKind.Sequential)] struct SidAndAttributes { public IntPtr Sid; public uint Attributes; }
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool CreateRestrictedToken(IntPtr original, uint flags,
        uint disabledCount, ref SidAndAttributes disabled, uint deletedCount, IntPtr deleted, uint restrictedCount, IntPtr restricted, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool ImpersonateLoggedOnUser(IntPtr token);
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool RevertToSelf();
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);

    internal static int WriteProbe(string target)
    {
        if (new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator)) return 24;
        try
        {
            var folder = Path.Combine(target, "ordinary-user-data");
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, "settings.json");
            File.WriteAllText(path, "first");
            File.WriteAllText(path, "updated");
            if (File.ReadAllText(path) != "updated") return 25;
            File.Move(path, path + ".moved");
            File.Delete(path + ".moved");
            Directory.Delete(folder);
            var retained = Path.Combine(target, "models", "retention-fixture.txt");
            if (File.Exists(retained)) File.WriteAllText(retained, File.ReadAllText(retained));
            return 0;
        }
        catch (UnauthorizedAccessException) { return 23; }
    }

    internal static int Run(string target)
    {
        var sid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var bytes = new byte[sid.BinaryLength];
        sid.GetBinaryForm(bytes, 0);
        var pointer = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, pointer, bytes.Length);
        IntPtr original = IntPtr.Zero, token = IntPtr.Zero;
        bool impersonating = false;
        try
        {
            using var self = Process.GetCurrentProcess();
            if (!OpenProcessToken(self.Handle, 0xB, out original)) throw new Win32Exception();
            var disabled = new SidAndAttributes { Sid = pointer };
            if (!CreateRestrictedToken(original, 1, 1, ref disabled, 0, IntPtr.Zero, 0, IntPtr.Zero, out token)) throw new Win32Exception();
            if (!ImpersonateLoggedOnUser(token)) throw new Win32Exception();
            impersonating = true;
            // Synchronous file operations exercise Windows' real restricted-token ACL checks, even with EnableLUA=0.
            return WriteProbe(target);
        }
        finally
        {
            if (impersonating && !RevertToSelf()) throw new Win32Exception();
            if (token != IntPtr.Zero) CloseHandle(token);
            if (original != IntPtr.Zero) CloseHandle(original);
            Marshal.FreeHGlobal(pointer);
        }
    }
}
