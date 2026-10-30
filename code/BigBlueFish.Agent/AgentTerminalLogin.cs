using System.Diagnostics;

namespace BigBlueFish.Agent;

internal sealed class AgentAuthenticationCancelledException() : IOException("Agent 认证已取消；点击重新连接可重试。");

internal static class AgentTerminalLogin
{
    private static (string Command, string[] Arguments)? Terminal()
    {
        foreach (var (command, arguments) in new[] {
            ("gnome-terminal", new[] { "--wait", "--" }),
            ("konsole", new[] { "--nofork", "-e" }), ("xterm", new[] { "-e" }) })
            if (HarnessCatalog.FindExecutable(command) is { } path) return (path, arguments);
        return null;
    }

    public static bool CanLaunch(ProcessStartInfo configured) => OperatingSystem.IsWindows()
        ? configured.Arguments.Length == 0 && !new[] { "cmd", "powershell", "pwsh" }.Contains(Path.GetFileNameWithoutExtension(configured.FileName).ToLowerInvariant())
        : OperatingSystem.IsLinux() && Terminal() != null;

    public static async Task Run(ProcessStartInfo configured, AgentAuthenticationMethod method, CancellationToken token)
    {
        if (!CanLaunch(configured)) throw new IOException("当前 Agent 入口无法打开交互登录终端，请按安装与登录说明完成登录后重连。");
        var start = new ProcessStartInfo { FileName = configured.FileName, WorkingDirectory = configured.WorkingDirectory,
            UseShellExecute = false, CreateNoWindow = false };
        foreach (var pair in configured.Environment) start.Environment[pair.Key] = pair.Value;
        foreach (var pair in method.Environment) start.Environment[pair.Key] = pair.Value;
        if (!OperatingSystem.IsWindows())
        {
            var terminal = Terminal()!.Value; start.FileName = terminal.Command;
            foreach (var arg in terminal.Arguments) start.ArgumentList.Add(arg);
            start.ArgumentList.Add(configured.FileName);
        }
        foreach (var arg in configured.ArgumentList.Concat(method.Arguments)) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new IOException("无法打开 Agent 登录终端。");
        using var ownership = new ProcessOwnership(process);
        try { await process.WaitForExitAsync(token); }
        catch { try { if (!process.HasExited) process.Kill(true); } catch (Exception) { } throw; }
        if (process.ExitCode != 0) throw new IOException("Agent 登录终端未成功完成，退出代码：" + process.ExitCode + "。请重新连接后重试。");
    }
}
