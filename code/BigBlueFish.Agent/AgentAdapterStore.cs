using System.Diagnostics;
using System.Text.Json;

namespace BigBlueFish.Agent;

public sealed class AgentAdapterStore(string root)
{
    public string? FindExecutable(HarnessDefinition harness)
    {
        var native = harness.FindExecutable();
        if (native != null) return native;
        string cached = Path.Combine(root, "adapters", "antigravity", OperatingSystem.IsWindows() ? "agy_acp_server.exe" : "agy_acp_server.par");
        return harness.Id == "antigravity" && File.Exists(cached) ? cached : null;
    }

    public static string? AntigravityHelper(string binary)
    {
        string helper = Path.Combine(Path.GetDirectoryName(binary)!, OperatingSystem.IsWindows() ? "localharness_external.exe" : "localharness_external");
        return File.Exists(helper) ? helper : null;
    }

    public async Task<(string Path, string[] Arguments)> Resolve(HarnessDefinition harness, string executable, CancellationToken token, Action<string> status)
    {
        if (harness.Id == "antigravity") return await Antigravity(executable, token, status);
        if (harness.AdapterPackage == null) return (executable, harness.Arguments);
        string? existing = HarnessCatalog.FindExecutable(harness.AdapterCommand!);
        if (existing != null) return (existing, []);
        var npm = HarnessCatalog.FindExecutable("npm") ?? throw new IOException("此 Harness 的 ACP 适配器需要 Node.js / npm，请先安装 Node.js。");
        string folder = Path.Combine(root, "adapters", harness.Id);
        string command = Path.Combine(folder, "node_modules", ".bin", harness.AdapterCommand! + (OperatingSystem.IsWindows() ? ".cmd" : ""));
        string receipt = Path.Combine(folder, "fish-adapter.json");
        if (File.Exists(command) && File.Exists(receipt) && File.ReadAllText(receipt) == harness.AdapterPackage) return (command, []);
        Directory.CreateDirectory(folder);
        status("正在安装 " + harness.Name + " 的 ACP 适配器…");
        var start = AgentProcess.StartInfo(npm, ["install", "--prefix", folder, "--no-audit", "--no-fund", "--save-exact", "--registry", "https://registry.npmjs.org", "--cache", Path.Combine(root, "adapter-cache"), harness.AdapterPackage], folder);
        using var process = Process.Start(start) ?? throw new IOException("无法启动适配器安装。");
        using var ownership = new ProcessOwnership(process);
        var stdout = process.StandardOutput.ReadToEndAsync(token); var stderr = process.StandardError.ReadToEndAsync(token);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromMinutes(5));
        try { await process.WaitForExitAsync(deadline.Token); }
        catch { try { process.Kill(true); } catch (Exception) { } throw; }
        await stdout; string error = await stderr;
        if (process.ExitCode != 0 || !File.Exists(command)) throw new IOException("ACP 适配器安装失败。请检查 npm 网络连接。" + error[^Math.Min(500, error.Length)..]);
        AtomicText.Write(receipt, harness.AdapterPackage);
        return (command, []);
    }

    private async Task<(string, string[])> Antigravity(string executable, CancellationToken token, Action<string> status)
    {
        // Google's official ACP distribution is a separate executable from agy.
        string? existing = HarnessCatalog.FindExecutable("agy_acp_server");
        if (existing != null && AntigravityHelper(existing) != null) return (existing, []);
        string platform = OperatingSystem.IsWindows() ? "windows-x86_64" : OperatingSystem.IsMacOS() ? "darwin-aarch64" : "linux-x86_64";
        string folder = Path.Combine(root, "adapters", "antigravity");
        string binary = Path.Combine(folder, OperatingSystem.IsWindows() ? "agy_acp_server.exe" : "agy_acp_server.par");
        string receipt = Path.Combine(folder, "distribution.json");
        if (File.Exists(binary) && AntigravityHelper(binary) != null && File.Exists(receipt))
            return (binary, JsonSerializer.Deserialize<string[]>(File.ReadAllText(receipt)) ?? []);
        status("正在准备 Antigravity 官方 ACP 组件…");
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
        string registry = await http.GetStringAsync("https://raw.githubusercontent.com/agentclientprotocol/registry/main/antigravity-acp/agent.json", token);
        using var json = JsonDocument.Parse(registry);
        var artifact = json.RootElement.GetProperty("distribution").GetProperty("binary").GetProperty(platform);
        var url = new Uri(artifact.GetProperty("archive").GetString()!);
        if (url.Scheme != "https" || url.Host != "dl.google.com") throw new IOException("Antigravity ACP 下载地址无效。");
        byte[] bytes = await http.GetByteArrayAsync(url, token);
        string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
        if (!hash.Equals(artifact.GetProperty("sha256").GetString(), StringComparison.OrdinalIgnoreCase)) throw new IOException("Antigravity ACP 组件校验失败。");
        Directory.CreateDirectory(folder);
        using var archive = new System.IO.Compression.ZipArchive(new MemoryStream(bytes));
        string helperName = OperatingSystem.IsWindows() ? "localharness_external.exe" : "localharness_external";
        string[] required = [helperName, Path.GetFileName(binary)];
        if (required.Any(name => !archive.Entries.Any(e => e.FullName == name)))
            throw new IOException("Antigravity 包内缺少 ACP 程序或配套运行程序。");
        // Both files belong to the same verified official release. Publish the server last.
        foreach (string name in required)
        {
            string target = Path.Combine(folder, name), temporary = target + "." + Guid.NewGuid().ToString("N") + ".download";
            try
            {
                await using (var input = archive.GetEntry(name)!.Open())
                await using (var output = File.Create(temporary)) await input.CopyToAsync(output, token);
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                File.Move(temporary, target, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        string[] arguments = artifact.TryGetProperty("args", out var args) ? args.EnumerateArray().Select(a => a.GetString()!).ToArray() : [];
        AtomicText.Write(receipt, JsonSerializer.Serialize(arguments));
        return (binary, arguments);
    }
}
