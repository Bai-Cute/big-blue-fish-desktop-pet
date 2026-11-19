using System.Diagnostics;
using System.Reflection;
using BigBlueFish.Agent;

if (!OperatingSystem.IsLinux()) throw new Exception("Run this reproduction on Linux.");
string root = Path.GetFullPath(args[0]); Directory.CreateDirectory(root);
if (args.Contains("--inventory"))
{
    Console.WriteLine("Resolving actual OpenCode through the product discovery path");
    string binary = HarnessCatalog.Get("opencode").FindExecutable() ?? throw new Exception("Actual CLI missing");
    Console.WriteLine("CLI found; starting actual ACP process");
    var start = AgentProcess.StartInfo(binary, ["acp", "--print-logs"], root);
    await using var client = new AcpConnection(start, (_, _) => Task.FromResult<string?>(null));
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
    await client.Initialize(deadline.Token);
    Console.WriteLine("PASS actual product discovery, launcher and ACP client: " + client.AgentName + " " + client.AgentVersion);
    return;
}
string bin = Path.Combine(root, "terminal only", "bin"); Directory.CreateDirectory(bin);
string executable = Path.Combine(bin, "opencode"), shell = Path.Combine(root, "login-shell");
static string Quote(string value) => "'" + value.Replace("'", "'\"'\"'") + "'";
File.WriteAllText(executable, "#!/bin/sh\nprintf 'opencode fixture\\n'\nsibling-helper\n");
File.WriteAllText(Path.Combine(bin, "sibling-helper"), "#!/bin/sh\nprintf 'helper found\\n'\n");
File.WriteAllText(shell, "#!/bin/sh\n[ \"$1\" = '-ic' ] || exit 9\nprintf 'startup banner\\n<bbf-path>%s</bbf-path>\\n' " + Quote(bin) + "\n");
foreach (string file in new[] { executable, Path.Combine(bin, "sibling-helper"), shell })
    File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
Environment.SetEnvironmentVariable("PATH", "/usr/bin:/bin");
Environment.SetEnvironmentVariable("SHELL", shell);
int checks = 0;
void Check(bool value, string label) { if (!value) throw new Exception(label); Console.WriteLine("PASS " + label); checks++; }
Check(HarnessCatalog.FindExecutable("opencode", "/usr/bin:/bin") == null, "old desktop-only PATH reproduces missing OpenCode");
Check(HarnessCatalog.Get("opencode").FindExecutable() == executable, "interactive login PATH discovers CLI outside desktop PATH");
File.WriteAllText(Path.Combine(bin,"printf"), "#!/bin/sh\nexit 0\n");
Check(HarnessCatalog.FindExecutable("printf") == Path.Combine(bin,"printf"), "shell PATH precedence matches terminal when executable names overlap");
using (var process = Process.Start(AgentProcess.StartInfo(executable, ["acp"], root))!) {
    string text = await process.StandardOutput.ReadToEndAsync();
    await process.WaitForExitAsync();
    Check(process.ExitCode == 0 && text.Contains("helper found"), "launched CLI inherits same resolved PATH for its helper processes");
}
Check(HarnessCatalog.FindExecutable("not-installed-cli") == null, "missing CLI remains missing");
Check(HarnessCatalog.FindExecutable("opencode", root) == null, "explicit search path stays isolated");
Check(Environment.GetEnvironmentVariable("PATH") == "/usr/bin:/bin", "process-wide environment is unchanged");
File.WriteAllText(shell, "#!/bin/sh\nprintf '<bbf-path>%s</bbf-path>\\n' /usr/bin\n");
HarnessEnvironment.Refresh();
Check(HarnessCatalog.Get("opencode").FindExecutable() == null, "explicit rescan immediately refreshes changed shell PATH");
var read = typeof(HarnessEnvironment).GetMethod("ReadShellPath", BindingFlags.NonPublic | BindingFlags.Static)!;
Check((string)read.Invoke(null, ["/no/such/shell"])! == "", "missing login shell degrades safely");
string home = Path.Combine(root,"bash-home"); Directory.CreateDirectory(home);
File.WriteAllText(Path.Combine(home,".bashrc"), "export PATH=" + Quote(bin) + ":\"$PATH\"\n");
Environment.SetEnvironmentVariable("HOME",home); Environment.SetEnvironmentVariable("SHELL","/bin/bash"); HarnessEnvironment.Refresh();
Check(HarnessCatalog.Get("opencode").FindExecutable() == executable, "real Bash reads isolated .bashrc exactly as an interactive terminal does");
string hanging = Path.Combine(root, "hanging-shell"); File.WriteAllText(hanging, "#!/bin/sh\nsleep 30\n");
File.SetUnixFileMode(hanging, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
var watch = Stopwatch.StartNew(); string result = (string)read.Invoke(null, [hanging])!;
Check(result == "" && watch.Elapsed.TotalSeconds < 6, "stalled shell initialization is bounded and cancelled");
Console.WriteLine($"RESULT {checks} Linux desktop/terminal PATH checks passed");
