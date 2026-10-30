using System.Diagnostics;

namespace BigBlueFish.Agent;

public sealed record HarnessDefinition(string Id, string Name, string[] Commands, string[] Arguments,
    string Website, string SkillDirectory = ".agents/skills", string? AdapterPackage = null, string? AdapterCommand = null)
{
    public string? FindExecutable() => Commands.Select(c => HarnessCatalog.FindExecutable(c)).FirstOrDefault(p => p != null);
}

public static class HarnessCatalog
{
    // Adapter versions are intentionally pinned. A CLI being present does not prove its ACP implementation works.
    public static IReadOnlyList<HarnessDefinition> All { get; } = [
        new("claude", "Claude Code", ["claude"], [], "https://code.claude.com/docs/en/overview", ".claude/skills", "@agentclientprotocol/claude-agent-acp@0.99.0", "claude-agent-acp"),
        new("codex", "Codex CLI", ["codex"], [], "https://developers.openai.com/codex/cli", ".agents/skills", "@agentclientprotocol/codex-acp@2.5.0", "codex-acp"),
        new("gemini", "Gemini CLI", ["gemini"], ["--acp", "--allowed-tools", "mcp_big-blue-fish_*"], "https://geminicli.com", ".gemini/skills"),
        new("dsh", "DeepSeek Harness", ["dsh"], ["--profile", "acp"], "https://github.com/deepseek-ai/deepseek-harness"),
        new("kimi", "Kimi Code", ["kimi"], ["acp"], "https://moonshotai.github.io/kimi-cli", ".agents/skills"),
        new("qwen", "Qwen Code", ["qwen"], ["--acp"], "https://qwenlm.github.io/qwen-code-docs", ".qwen/skills"),
        new("antigravity", "Antigravity CLI", ["agy", "agy_acp_server"], [], "https://antigravity.google/docs/cli/overview/", ".agents/skills"),
        new("copilot", "GitHub Copilot CLI", ["copilot"], ["--acp"], "https://docs.github.com/en/copilot/how-tos/copilot-cli"),
        new("cursor", "Cursor CLI", ["cursor-agent", "agent"], ["acp"], "https://cursor.com/docs/cli", ".cursor/skills"),
        new("opencode", "OpenCode", ["opencode"], ["acp"], "https://opencode.ai/docs", ".agents/skills"),
        new("cline", "Cline CLI", ["cline"], ["--acp"], "https://docs.cline.bot/cline-cli", ".agents/skills"),
        new("codebuddy", "CodeBuddy Code", ["codebuddy"], ["--acp"], "https://www.codebuddy.ai/docs/cli", ".codebuddy/skills"),
        new("qoder", "Qoder CLI", ["qoder", "qodercli"], ["--acp"], "https://docs.qoder.com/cli", ".qoder/skills"),
        new("trae", "TRAE CLI", ["traecli"], ["acp", "serve"], "https://docs.trae.cn/cli_command-line-parameters", ".trae/skills"),
        new("kiro", "Kiro CLI", ["kiro-cli"], ["acp"], "https://kiro.dev/docs/cli", ".kiro/skills"),
        new("pi", "Pi", ["pi"], [], "https://github.com/earendil-works/pi", ".pi/skills", "pi-acp@0.0.34", "pi-acp"),
        new("devin", "Devin CLI", ["devin"], ["acp"], "https://docs.devin.ai/cli"),
        new("grok", "Grok Build", ["grok"], ["agent", "stdio"], "https://docs.x.ai/build/overview"),
        new("hermes", "Hermes Agent", ["hermes"], ["acp"], "https://hermes-agent.nousresearch.com/docs", ".agents/skills"),
        new("openclaw", "OpenClaw", ["openclaw"], ["acp"], "https://docs.openclaw.ai/cli/acp"),
        new("minimax", "MiniMax Code", ["mcode"], ["acp"], "https://www.npmjs.com/package/@minimax-ai/code")
    ];

    public static HarnessDefinition Get(string id) => All.FirstOrDefault(x => x.Id == id)
        ?? throw new ArgumentException("请选择支持的 Harness。");

    public static string? FindExecutable(string command, string? path = null)
    {
        if (Path.IsPathRooted(command)) return File.Exists(command) ? Path.GetFullPath(command) : null;
        var suffixes = OperatingSystem.IsWindows() ? new[] { ".exe", ".cmd", ".bat", ".ps1", "" } : [""];
        string search = path ?? Environment.GetEnvironmentVariable("PATH") ?? "";
        if (path == null && OperatingSystem.IsWindows())
            search += Path.PathSeparator + Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User)
                + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine);
        foreach (var folder in search.Split(Path.PathSeparator).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(folder)) continue;
            foreach (var suffix in suffixes)
            {
                try
                {
                    var candidate = Path.GetFullPath(Path.Combine(Environment.ExpandEnvironmentVariables(folder.Trim().Trim('"')), command + suffix));
                    if (File.Exists(candidate)) return candidate;
                }
                catch (Exception e) when (e is ArgumentException or NotSupportedException or IOException) { }
            }
        }
        return null;
    }
}

public static class AgentProcess
{
    public static ProcessStartInfo StartInfo(string executable, IEnumerable<string> arguments, string cwd)
    {
        var args = arguments.ToArray();
        var start = new ProcessStartInfo { FileName = executable, WorkingDirectory = cwd, UseShellExecute = false,
            CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new System.Text.UTF8Encoding(false), StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8 };
        if (OperatingSystem.IsWindows() && Path.GetExtension(executable).Equals(".ps1", StringComparison.OrdinalIgnoreCase))
        {
            start.FileName = "powershell.exe";
            foreach (var x in new[] { "-NoProfile", "-NonInteractive", "-File", executable }.Concat(args)) start.ArgumentList.Add(x);
        }
        else if (OperatingSystem.IsWindows() && new[] { ".cmd", ".bat" }.Contains(Path.GetExtension(executable).ToLowerInvariant()))
        {
            // npm shims normally delegate directly to a Node entry point; bypass cmd quoting when possible.
            var text = File.ReadAllText(executable);
            var match = System.Text.RegularExpressions.Regex.Match(text, "\"%dp0%[\\\\/](?<script>[^\"]+\\.([cm]?js))\"");
            var node = HarnessCatalog.FindExecutable("node");
            if (match.Success && node != null)
            {
                start.FileName = node;
                start.ArgumentList.Add(Path.Combine(Path.GetDirectoryName(executable)!, match.Groups["script"].Value.Replace('\\', Path.DirectorySeparatorChar)));
                foreach (var x in args) start.ArgumentList.Add(x);
            }
            else
            {
                // Only fixed adapter arguments and local paths enter this shell. User messages always use stdin JSON.
                static string Quote(string value)
                {
                    if (value.IndexOfAny(['"', '\r', '\n', '%', '!']) >= 0) throw new IOException("CLI 启动参数含有不支持的 shell 字符。");
                    return "\"" + value + "\"";
                }
                start.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
                start.Arguments = "/d /s /c \"" + string.Join(" ", new[] { executable }.Concat(args).Select(Quote)) + "\"";
            }
        }
        else foreach (var x in args) start.ArgumentList.Add(x);
        start.Environment["NO_COLOR"] = "1";
        return start;
    }
}
