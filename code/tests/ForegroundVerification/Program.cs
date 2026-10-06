using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using VPet_Simulator.Windows;

class Program
{
    sealed class Fixture : Form
    {
        protected override bool ShowWithoutActivation => true;
        internal Fixture()
        {
            Text = "蓝色大肥鱼 · 前台窗口回归样例";
            Width = 420; Height = 180; ShowInTaskbar = false;
            Controls.Add(new Label { Dock = DockStyle.Fill, Text = "仅用于验证可见、最小化、隐藏窗口的截图行为" });
        }
    }
    static int checks;
    static void Check(bool value, string text)
    {
        if (!value) throw new Exception(text);
        checks++;
        Console.WriteLine("PASS " + text);
    }
    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--fixture")
        {
            using var form = new Fixture();
            form.Shown += (_, _) =>
            {
                Console.WriteLine(form.Handle.ToInt64()); Console.Out.Flush();
                _ = Task.Run(() =>
                {
                    while (Console.ReadLine() is { } command)
                        form.BeginInvoke((Action)(() =>
                        {
                            if (command == "minimize") form.WindowState = FormWindowState.Minimized;
                            if (command == "restore") { form.WindowState = FormWindowState.Normal; form.Show(); }
                            if (command == "hide") form.Hide();
                            if (command == "quit") { form.Close(); return; }
                            Console.WriteLine("ok"); Console.Out.Flush();
                        }));
                });
            };
            Application.Run(form);
            return 0;
        }
        var start = new ProcessStartInfo(Environment.ProcessPath!)
        { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, CreateNoWindow = true };
        if (Path.GetFileNameWithoutExtension(Environment.ProcessPath!) == "dotnet")
            start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        start.ArgumentList.Add("--fixture");
        using var child = Process.Start(start)!;
        string Read()
        {
            var line = child.StandardOutput.ReadLineAsync();
            if (!line.Wait(10000)) throw new Exception("Fixture timed out");
            return line.Result ?? throw new Exception("Fixture exited");
        }
        void Command(string text) { child.StandardInput.WriteLine(text); child.StandardInput.Flush(); Read(); }
        try
        {
            var handle = new IntPtr(long.Parse(Read()));
            using var own = new Fixture(); own.Show();
            var desktop = CompanionNative.GetShellWindow();
            Check(CompanionNative.IsExternalForegroundCandidate(handle), "visible external window accepted");
            Check(CompanionScreenCapture.Capture(handle) != null, "visible external window captured");
            Check(CompanionNative.ResolveSpeechForeground(handle, desktop) == handle, "current foreground beats old record");
            Command("minimize");
            Check(!CompanionNative.IsExternalForegroundCandidate(handle), "minimized record rejected");
            Check(CompanionScreenCapture.Capture(handle) == null, "minimized window never captured");
            Check(CompanionNative.ResolveSpeechForeground(own.Handle, handle) == desktop, "pet focus plus minimized record resolves to desktop");
            Check(CompanionNative.ResolveSpeechForeground(desktop, handle) == desktop, "actual desktop beats minimized record");
            Check(CompanionNative.ResolveSpeechForeground(IntPtr.Zero, handle) == IntPtr.Zero, "missing foreground never reuses old record");
            Check(CompanionScreenCapture.Capture(desktop) is { Width: > 100, Height: > 100 }, "desktop fallback has a real screenshot");
            Command("restore");
            Check(CompanionNative.IsExternalForegroundCandidate(handle), "restored window becomes usable");
            Command("hide");
            Check(!CompanionNative.IsExternalForegroundCandidate(handle), "hidden window rejected");
            Check(CompanionScreenCapture.Capture(handle) == null, "hidden window never captured");
            child.StandardInput.WriteLine("quit"); child.StandardInput.Flush();
            Check(child.WaitForExit(5000), "fixture exited");
            Check(!CompanionNative.IsExternalForegroundCandidate(handle), "destroyed window rejected");
            Console.WriteLine("RESULT " + checks + " foreground/capture regression checks passed");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
        finally { if (!child.HasExited) { child.Kill(); child.WaitForExit(5000); } }
    }
}
