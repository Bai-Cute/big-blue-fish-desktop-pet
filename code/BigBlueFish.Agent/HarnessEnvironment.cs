using System.Diagnostics;

namespace BigBlueFish.Agent;

// Desktop launchers do not inherit the PATH configured by an interactive shell.
public static class HarnessEnvironment
{
    static readonly object gate = new();
    static string shellPath = "";
    static long refreshed;
    public static void Refresh() { lock (gate) refreshed = 0; }

    public static string SearchPath
    {
        get
        {
            string inherited = Environment.GetEnvironmentVariable("PATH") ?? "";
            if (OperatingSystem.IsWindows()) return inherited;
            lock (gate)
            {
                if (refreshed == 0 || Environment.TickCount64 - refreshed > 60_000)
                {
                    shellPath = ReadShellPath(UserShell());
                    refreshed = Environment.TickCount64;
                }
                return shellPath.Length == 0 ? inherited : shellPath + Path.PathSeparator + inherited;
            }
        }
    }

    static string UserShell()
    {
        if (Environment.GetEnvironmentVariable("SHELL") is { Length: > 0 } configured) return configured;
        if (OperatingSystem.IsLinux())
        {
            try
            {
                foreach (string line in File.ReadLines("/etc/passwd"))
                {
                    var fields = line.Split(':');
                    if (fields.Length >= 7 && fields[0] == Environment.UserName && Path.IsPathRooted(fields[6])) return fields[6];
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            return File.Exists("/bin/bash") ? "/bin/bash" : "/bin/sh";
        }
        return "/bin/zsh";
    }

    internal static string ReadShellPath(string shell)
    {
        try
        {
            var start = new ProcessStartInfo(shell) { UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
            // An interactive shell launched from a background terminal job can
            // SIGTTIN-stop its whole process group, including the desktop app.
            // A detached session has the same no-terminal environment as a desktop launcher.
            if (OperatingSystem.IsLinux() && File.Exists("/usr/bin/setsid"))
            {
                start.FileName = "/usr/bin/setsid";
                start.ArgumentList.Add("--wait");
                start.ArgumentList.Add(shell);
            }
            // Fixed command only: no prompt, CLI name or user data is interpreted by the shell.
            foreach (var arg in new[] { OperatingSystem.IsMacOS() ? "-ilc" : "-ic", "printf '\\n<bbf-path>%s</bbf-path>\\n' \"$PATH\"" }) start.ArgumentList.Add(arg);
            using var process = Process.Start(start)!;
            process.StandardInput.Close();
            var stdout = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(4000)) { process.Kill(true); return ""; }
            // A background process spawned by a startup script may retain the output pipe.
            if (!stdout.Wait(500)) return "";
            var match = System.Text.RegularExpressions.Regex.Match(stdout.Result, @"<bbf-path>([^\r\n]*)</bbf-path>");
            return process.ExitCode == 0 && match.Success ? match.Groups[1].Value : "";
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or IOException or InvalidOperationException or AggregateException)
        { return ""; }
    }
}
