using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Windows;
using System.Windows.Threading;
using BigBlueFish.Windows;

internal static class TrayVerification
{
    [StructLayout(LayoutKind.Sequential)] struct SidAndAttributes { internal IntPtr Sid; internal uint Attributes; }
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool CreateRestrictedToken(IntPtr original, uint flags, uint count, ref SidAndAttributes disabled,
        uint deletedCount, IntPtr deleted, uint restrictedCount, IntPtr restricted, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool ImpersonateLoggedOnUser(IntPtr token);
    [DllImport("advapi32.dll")] static extern bool RevertToSelf();
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr w, IntPtr l);
    internal static Task<int> Run(string root)
    {
        var completion = new TaskCompletionSource<int>();
        var thread = new Thread(() =>
        {
            IntPtr sidMemory = IntPtr.Zero, original = IntPtr.Zero, restricted = IntPtr.Zero;
            CompanionTrayIcon? tray = null;
            bool impersonating = false;
            try
            {
                Directory.CreateDirectory(root); Directory.SetCurrentDirectory(Path.GetFullPath(root));
                using var process = Process.GetCurrentProcess();
                var sid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
                byte[] bytes = new byte[sid.BinaryLength]; sid.GetBinaryForm(bytes, 0);
                sidMemory = Marshal.AllocHGlobal(bytes.Length); Marshal.Copy(bytes, 0, sidMemory, bytes.Length);
                if (!OpenProcessToken(process.Handle, 0xB, out original)) throw new Exception("OpenProcessToken failed");
                var disabled = new SidAndAttributes { Sid = sidMemory };
                if (!CreateRestrictedToken(original, 1, 1, ref disabled, 0, IntPtr.Zero, 0, IntPtr.Zero, out restricted)
                    || !ImpersonateLoggedOnUser(restricted)) throw new Exception("Cannot establish ordinary-user token");
                impersonating = true;
                if (new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
                    throw new Exception("Test still has administrator membership");
                var app = new System.Windows.Application();
                bool fail = true;
                tray = new CompanionTrayIcon((operation, _) => operation == 2 || !fail)
                { Icon = new Icon(Path.Combine(AppContext.BaseDirectory, "BigBlueFish.ico"), 32, 32), Text = "BigBlueFish registration fault test" };
                tray.Visible = true;
                if (tray.Registered || !File.Exists("companion-errors.log")) throw new Exception("Failed Shell registration was silently accepted");
                fail = false; tray.EnsureRegistered();
                if (!tray.Registered || !File.ReadAllText("companion-errors.log").Contains("已恢复")) throw new Exception("Registration retry did not recover");
                tray.Dispose();
                tray = new CompanionTrayIcon { Icon = new Icon(Path.Combine(AppContext.BaseDirectory, "BigBlueFish.ico"), 32, 32), Text = "蓝色大肥鱼普通权限托盘验证" };
                tray.Visible = true;
                var clock = Stopwatch.StartNew();
                while (!tray.IsPresent() && clock.Elapsed.TotalSeconds < 8)
                {
                    var frame = new DispatcherFrame();
                    var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
                    timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
                    tray.EnsureRegistered();
                }
                if (!tray.Registered || !tray.IsPresent()) throw new Exception("Windows did not expose the real ordinary-user tray icon");
                bool restored = false, menuOpened = false;
                tray.DoubleClick += (_, _) => restored = true;
                tray.ContextMenuStrip = new CompanionTrayMenu();
                tray.ContextMenuStrip.Items.Add("显示桌宠");
                tray.ContextMenuStrip.Opened += (_, _) => menuOpened = true;
                SendMessage(tray.Handle, 0x8001, new IntPtr(1), new IntPtr(0x202));
                if (!restored) throw new Exception("Native left click did not reach restore callback");
                SendMessage(tray.Handle, 0x8001, new IntPtr(1), new IntPtr(0x205));
                if (!menuOpened || !tray.ContextMenuStrip.Visible) throw new Exception("Native right click did not open styled menu");
                tray.ContextMenuStrip.Close();
                tray.Visible = false;
                if (tray.IsPresent()) throw new Exception("Hidden tray icon remains registered");
                tray.Visible = true;
                if (!tray.IsPresent()) throw new Exception("Tray icon failed to register again");
                tray.Dispose();
                if (tray.Registered) throw new Exception("Disposed tray still registered");
                Console.WriteLine("RESULT ordinary-user token verified; failed Shell add detected/logged/retried; actual Shell icon rectangle present; native left/right callbacks; hide/show/dispose passed");
                completion.SetResult(0);
            }
            catch (Exception e) { Console.Error.WriteLine(e); completion.SetResult(1); }
            finally
            {
                tray?.Dispose(); if (impersonating) RevertToSelf();
                if (restricted != IntPtr.Zero) CloseHandle(restricted); if (original != IntPtr.Zero) CloseHandle(original);
                if (sidMemory != IntPtr.Zero) Marshal.FreeHGlobal(sidMemory);
            }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return completion.Task;
    }
}
