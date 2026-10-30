using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using BigBlueFish.Agent;
using BigBlueFish.Control;

if (args.Contains("--fixture-login")) { File.WriteAllText("fixture-terminal-authenticated", Environment.GetEnvironmentVariable("FISH_LOGIN_TEST") ?? "missing"); return; }
if (args.Contains("--fixture")) { await Fixture.Run(); return; }
if (args.Length >= 3 && args[0] == "--auth-inventory")
{
    var harness = HarnessCatalog.Get(args[1]); var adapters = new AgentAdapterStore(args[2]);
    string executable = adapters.FindExecutable(harness) ?? throw new IOException("CLI not installed");
    var command = await adapters.Resolve(harness, executable, CancellationToken.None, Console.WriteLine);
    var start = AgentProcess.StartInfo(command.Path, command.Arguments, Path.GetFullPath(".work"));
    if (harness.Id == "codex") start.Environment["CODEX_PATH"] = executable;
    await using var client = new AcpConnection(start, (_, _) => Task.FromResult<string?>(null));
    await client.Initialize(CancellationToken.None);
    Console.WriteLine(JsonSerializer.Serialize(new {harness = harness.Id, agent = client.AgentName, version = client.AgentVersion, methods = client.AuthenticationMethods}));
    return;
}
if (args.Length >= 3 && args[0] == "--probe") { Environment.ExitCode = await LiveProbe.Run(args[1], args[2], args.Contains("--prompt"), args.Contains("--configure")); return; }
int passed = 0;
void Check(bool condition, string label) { if (!condition) throw new Exception("FAIL " + label); passed++; Console.WriteLine("PASS " + label); }
async Task Until(Func<bool> predicate, string label, int seconds = 15)
{
    var limit = DateTime.UtcNow.AddSeconds(seconds);
    while (!predicate() && DateTime.UtcNow < limit) await Task.Delay(30);
    Check(predicate(), label);
}
string root = Path.GetFullPath(args.FirstOrDefault(a => !a.StartsWith("--")) ?? Path.Combine(".work", "agent-verification-" + Guid.NewGuid().ToString("N")));
Directory.CreateDirectory(root);
WorkspaceVerification.Run(Path.Combine(root, "workspace-rules"), Check);
Check(HarnessCatalog.All.Count == 21 && HarnessCatalog.All.Select(x => x.Id).Distinct().Count() == 21, "21 distinct selected harnesses");
Check(HarnessCatalog.Get("kimi").Arguments.SequenceEqual(["acp"]), "Kimi native ACP launch");
Check(HarnessCatalog.Get("codex").AdapterPackage != null && HarnessCatalog.Get("claude").AdapterPackage != null && HarnessCatalog.Get("pi").AdapterPackage != null, "three explicit ACP adapters");
Check(HarnessCatalog.FindExecutable("fish-no-such-cli", root) == null, "missing CLI is not invented");
var legacyAuth = AgentAuthenticationMethod.Parse(JsonSerializer.SerializeToElement(new {authMethods = new[] {new {id="login",name="Kimi login",_meta=new Dictionary<string,object>{["terminal-auth"]=new {command="untrusted-command",args=new[]{"login"},env=new Dictionary<string,string>(),type="terminal"}}}}}), true);
Check(legacyAuth.Length == 1 && legacyAuth[0].Type == "terminal" && legacyAuth[0].ReplaceArguments && legacyAuth[0].Arguments.SequenceEqual(["login"]), "Kimi legacy terminal-auth normalized without adopting server command");
Check(AgentAuthenticationMethod.Parse(JsonSerializer.SerializeToElement(new {authMethods = new[] {new {id="login",type="terminal"}}}), false).Length == 0, "terminal authentication omitted where interactive terminal cannot be provided");
Check(new AcpException(JsonSerializer.SerializeToElement(new {code=-32603,message="Session expired",data=new {errorKind="authentication_failed"}})).AuthenticationRequired
    && !new AcpException(JsonSerializer.SerializeToElement(new {code=-32603,message="Session expired",data=new {errorKind="authentication_failed"}})).SessionMissing, "structured expired authentication never mistaken for a missing conversation");
var skills = new AgentSkillStore(Path.Combine(root, "skill-test"), HarnessCatalog.Get("claude")); skills.Install();
Check(File.Exists(skills.SkillPath) && File.ReadAllText(skills.SkillPath).Contains("name: big-blue-fish"), "actual workspace skill installed");
Check(skills.DirectoryPath.Contains(".claude"), "Claude skill uses project discovery directory");
AtomicText.Write(skills.PersonaPath, "user persona"); skills.Install();
Check(File.ReadAllText(skills.PersonaPath) == "user persona", "user persona preserved on repeated install");
var jobsRoot = Path.Combine(root, "jobs"); var jobs = new AgentWakeups(jobsRoot);
var now = new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.FromHours(8));
var one = jobs.Schedule("kimi", "one", "care", 30, null, null, now);
var repeated = jobs.Schedule("kimi", "one", "care again", 60, 60, "repeat", now);
Check(jobs.PeekDue("kimi", "one", now.AddSeconds(29)).Length == 0, "reservation not early");
Check(jobs.PeekDue("kimi", "two", now.AddHours(2)).Length == 0, "reservations isolated by conversation");
Check(jobs.PeekDue("qwen", "one", now.AddHours(2)).Length == 0, "reservations isolated by harness");
jobs.Acknowledge(one, now.AddSeconds(30));
Check(jobs.List("kimi", "one").All(x => x.Id != one.Id), "one-shot delivered once");
jobs.Acknowledge(repeated, now.AddHours(8));
Check(jobs.List("kimi", "one").Single().Due == now.AddHours(8).AddSeconds(60), "sleep coalesces missed intervals without catch-up burst");
Check(new AgentWakeups(jobsRoot).List("kimi", "one").Length == 1, "reservation persists across host reconstruction");
bool bad = false; try { jobs.Schedule("kimi", "one", "x", 0, null, null, now); } catch (ArgumentException) { bad = true; }
Check(bad, "zero-delay runaway scheduling rejected");
jobs.Clear("kimi"); Check(jobs.List("kimi", "one").Length == 0, "daily-care disable can clear reservations");

string fixtureExe = Environment.ProcessPath!;
if (Path.GetFileNameWithoutExtension(fixtureExe) == "dotnet") throw new Exception("Run the generated apphost for process tests.");
string shimDirectory = Path.Combine(root, "shims"); Directory.CreateDirectory(shimDirectory);
string wrapper = Path.Combine(shimDirectory, OperatingSystem.IsWindows() ? "opencode.cmd" : "opencode");
AtomicText.Write(Path.Combine(shimDirectory, "fixture.cjs"), "require('child_process').spawn(" + JsonSerializer.Serialize(fixtureExe) + ",['--fixture',...process.argv.slice(2)],{stdio:'inherit'}).on('exit',c=>process.exit(c??1));");
AtomicText.Write(wrapper, OperatingSystem.IsWindows() ? "@echo off\r\nnode \"%dp0%\\fixture.cjs\"\r\n" : "#!/bin/sh\nexec \"" + fixtureExe + "\" --fixture\n");
if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(wrapper, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
var oldPath = Environment.GetEnvironmentVariable("PATH");
Environment.SetEnvironmentVariable("PATH", shimDirectory + Path.PathSeparator + oldPath);
string pipe = "BigBlueFish.AgentVerification." + Guid.NewGuid().ToString("N");
string fakeBridge = Path.Combine(root, "bridge-placeholder"); File.WriteAllText(fakeBridge, "fixture uses the configured pipe directly");
AgentCoordinator? host = null;
var said = new List<string>(); int permissions = 0, authentications = 0;
using var server = new ControlServer((name, values, token) => {
    if (name == "say") { host?.MarkSpoken(); lock (said) said.Add(values.GetProperty("text").GetString()!); return Task.FromResult(ControlTools.Text(new { displayed = true })); }
    return Task.FromResult(ControlTools.Text(host!.ExecuteTool(name, values)));
}, pipe);
try
{
    host = new AgentCoordinator(Path.Combine(root, "host"), fakeBridge, pipe, (request, token) => {
        if (request.Kind == "authentication")
        {
            authentications++; Check(request.Options.Count == 1 && request.Options[0].Id is "oauth-personal" or "terminal-login", "only supported advertised authentication method offered");
            if (File.Exists(Path.Combine(host!.Workspaces.Current, "fixture-auth-cancel"))) return Task.FromResult<string?>(null);
            return Task.FromResult<string?>(request.Options[0].Id);
        }
        permissions++; return Task.FromResult<string?>(request.Options.First(x => x.Kind == "allow_once").Id);
    }, Path.Combine(root, "workspace"));
    host.Changed += () => Console.WriteLine("STATE " + host.Status + " " + host.Detail);
    host.Configure("opencode", new(true, false, false));
    await Until(() => host.Connected, "ACP initialize + session/new from actual subprocess");
    string firstSession = host.SessionId;
    Check(host.Configuration.Options.Length == 4, "Agent session exposes live model mode thought and boolean options");
    await host.SetConfiguration("model", "accurate");
    Check(host.CurrentModel == "accurate" && host.Configuration.Options.Single(o => o.Id == "thought").Choices.Length == 3, "model ACK updates dependent reasoning choices");
    await host.SetConfiguration("permission_flag", "true");
    Check(host.Configuration.Options.Single(o => o.Id == "permission_flag").CurrentValue == "true", "boolean configuration uses ACP boolean request");
    bool invalid = false; try { await host.SetConfiguration("model", "invented"); } catch (ArgumentException) { invalid = true; }
    Check(invalid && host.CurrentModel == "accurate", "invalid unadvertised model rejected without optimistic success");
    Check(File.Exists(host.Skills.SkillPath), "skill installed before session prompt");
    Check(host.SendUser("hello"), "user message accepted");
    await Until(() => said.Count == 1 && !host.Busy, "actual MCP say controls visible text path");
    Check(said[0] == "tool-only response", "ordinary assistant output never becomes say");
    await Until(() => server.Connections > 0, "ACP session configured internal MCP connection");
    Check(host.SendUser("permission"), "permission turn accepted");
    await Until(() => permissions == 1 && said.Count == 2 && !host.Busy, "permission JSON-RPC response completes tool turn");
    host.Reconnect();
    await Until(() => host.Connected && host.SessionId == firstSession && !host.Busy, "reconnect retains fish session");
    await Task.Delay(250);
    Check(said.Count == 2, "replayed assistant history does not speak again");
    host.Configure("opencode", new(true, true, true));
    await Until(() => host.Wakeups.List("opencode", host.SessionId).Length == 1 && !host.Busy, "puppet plus daily care asks Agent to reserve wakeup");
    var reservation = host.Wakeups.List("opencode", host.SessionId).Single();
    host.Tick(reservation.Due.AddSeconds(1), true);
    // Host checks the real clock on dispatch, so replace the reservation with a deterministic due item for the integration turn.
    host.Wakeups.Clear();
    host.Wakeups.Schedule("opencode", host.SessionId, "test wake", 30, null, null, DateTimeOffset.Now.AddMinutes(-1));
    host.Tick(DateTimeOffset.Now, true);
    await Until(() => said.Count >= 3 && !host.Busy, "host clock starts a new ACP turn for reserved care");
    host.Configure("opencode", new(true, true, false));
    await Until(() => host.Wakeups.List("opencode", host.SessionId).Length == 1 && !host.Busy, "Agent timing remains enabled with local model as writer");
    int localRequests = 0; host.LocalSpeechRequested = _ => { localRequests++; return Task.CompletedTask; };
    host.Wakeups.Clear();
    host.Wakeups.Schedule("opencode", host.SessionId, "local generation", 30, null, null, DateTimeOffset.Now.AddMinutes(-1));
    int repliesBeforeLocal = said.Count;
    host.Tick(DateTimeOffset.Now, true);
    await Until(() => localRequests == 1 && !host.Busy, "Agent reservation invokes host local-generation callback");
    Check(said.Count == repliesBeforeLocal, "local-writer wakeup does not request an Agent-authored reply");
    int before = said.Count;
    Check(!host.DailyCare(), "puppet-only mode never starts ordinary daily care");
    Check(!host.ManualSpeech(false), "local writer does not send manual test to Agent");
    host.Configure("opencode", new(true, false, true)); host.DailyCare();
    Check(host.Wakeups.List("opencode", host.SessionId).Length == 0, "returning timing to fish clears Agent reservations");
    await Until(() => said.Count > before && !host.Busy, "ordinary host daily rhythm can delegate to Agent");
    host.Configure("opencode", new(true, true, true));
    await Until(() => !host.Busy && host.Wakeups.List("opencode", host.SessionId).Length == 1, "Agent trigger mode ready for manual entry tests");
    int manualBefore = said.Count;
    Check(host.ManualSpeech(false), "manual Agent speech test accepted while automatic timing is controlled by Agent");
    await Until(() => said.Count == manualBefore + 1 && !host.Busy, "manual Agent speech test reaches say tool");
    Check(host.ManualSpeech(true, "verified weather context"), "manual Agent weather accepted while automatic timing is controlled by Agent");
    await Until(() => said.Count == manualBefore + 2 && !host.Busy, "manual Agent weather reaches say tool");
    Check(File.ReadAllText(Path.Combine(host.Workspaces.Current, "fixture-last-prompt.txt")).Contains("verified weather context"), "Agent weather event includes actual host weather context");
    host.Configure("opencode", new(true, false, true));
    string previous = host.SessionId; host.NewConversation();
    await Until(() => host.Connected && host.SessionId != previous && !host.Busy, "new conversation receives different ID");
    string home = host.Workspaces.Current; File.WriteAllText(Path.Combine(home, "office.txt"), "work persists");
    string savedHome = host.SessionId; string second = Path.Combine(root, "second-workspace"); Directory.CreateDirectory(second);
    File.WriteAllText(Path.Combine(second, "fixture-legacy.flag"), "");
    await host.OpenWorkspace(second); await Until(() => host.Connected && host.Skills.Workspace == second, "switch workspace reconnects using selected cwd");
    Check(host.Configuration.Options.All(o => o.Method != "session/set_config_option"), "legacy-only Agent produces compatibility selectors");
    await host.SetConfiguration("$legacy-mode", "code");
    Check(host.Configuration.Options.Single(o => o.Category == "mode").CurrentValue == "code", "legacy mode change acknowledged");
    File.WriteAllText(Path.Combine(second, "fixture-mode-override"), "");
    await host.SetConfiguration("$legacy-mode", "code");
    Check(host.Configuration.Options.Single(o => o.Category == "mode").CurrentValue == "ask", "Agent-reported mode takes precedence over requested value");
    await host.OpenWorkspace(home); await Until(() => host.Connected && host.SessionId == savedHome, "return to workspace restores its own conversation");
    Check(File.ReadAllText(Path.Combine(home, "office.txt")) == "work persists", "conversation and workspace switches preserve user project files");
    host.SendUser("wait"); await Until(() => host.Busy, "workspace-switch cancellation has an active turn");
    var pendingConfig = host.SetConfiguration("model", "fast");
    await host.OpenWorkspace(second); await Until(() => host.Connected && host.Skills.Workspace == second && !host.Busy, "switching workspaces cancels old running turn");
    await Until(() => pendingConfig.IsCanceled, "configuration queued for old workspace is cancelled rather than applied to new one");
    await host.OpenWorkspace(home); await Until(() => host.Connected && host.SessionId == savedHome, "workspace cancellation preserves restorable conversation binding");
    File.WriteAllText(Path.Combine(home, "fixture-missing-session"), ""); host.Reconnect();
    await Until(() => host.Connected && host.SessionId != savedHome, "missing old conversation automatically creates replacement");
    File.Delete(Path.Combine(home, "fixture-missing-session"));
    string replacement = host.SessionId; string savedText = File.ReadAllText(host.Workspaces.SessionPath("opencode"));
    File.WriteAllText(Path.Combine(home, "fixture-auth-error"), ""); host.Reconnect();
    await Until(() => host.Status.StartsWith("需要登录"), "restore authentication error shown distinctly");
    Check(File.ReadAllText(host.Workspaces.SessionPath("opencode")) == savedText, "restore auth failure preserves conversation record");
    File.Delete(Path.Combine(home, "fixture-auth-error")); host.Reconnect();
    await Until(() => host.Connected && host.SessionId == replacement, "login retry restores original conversation after auth failure");
    host.SendUser("wait"); await Until(() => host.Busy, "long turn started"); host.Cancel();
    await Until(() => !host.Busy, "cancellation releases request gate");
    host.SendUser("after cancel"); await Until(() => host.Connected && !host.Busy && said.Count > before + 1, "cancelled runner replaced and session resumes");
    string beforeClear = host.SessionId; await host.ClearWorkspace(home);
    await Until(() => host.Connected && host.SessionId != beforeClear, "clear restarts session after removing old conversation bindings");
    Check(!File.Exists(Path.Combine(home,"office.txt")) && File.Exists(host.Skills.SkillPath), "clear removes user work and prepares fresh operation skill");
    string authWorkspace = Path.Combine(root, "authentication-workspace"); Directory.CreateDirectory(authWorkspace);
    File.WriteAllText(Path.Combine(authWorkspace, "fixture-auth-methods"), "");
    await host.OpenWorkspace(authWorkspace);
    await Until(() => host.Connected && authentications == 1, "authentication challenge retries session/new after authenticate");
    string authenticatedSession = host.SessionId;
    host.Reconnect();
    await Until(() => host.Connected && host.SessionId == authenticatedSession && authentications == 2, "authentication challenge retries session/load without losing saved conversation");
    Check(File.ReadAllText(Path.Combine(authWorkspace, "fixture-authenticated")) == "oauth-personal", "selected method sent using ACP authenticate methodId");
    File.WriteAllText(Path.Combine(authWorkspace, "fixture-prompt-auth"), "");
    int repliesBeforeAuthentication = said.Count;
    host.SendUser("authentication expires before generation");
    await Until(() => host.Connected && !host.Busy && said.Count == repliesBeforeAuthentication + 1 && authentications == 3, "expired prompt authentication restores session and retries unstarted turn exactly once");
    if (OperatingSystem.IsWindows())
    {
        string terminalWorkspace = Path.Combine(root, "terminal-workspace"); Directory.CreateDirectory(terminalWorkspace);
        File.WriteAllText(Path.Combine(terminalWorkspace, "fixture-terminal-method"), "");
        await host.OpenWorkspace(terminalWorkspace);
        await Until(() => host.Connected && authentications == 4, "terminal authentication reconnects and initializes original ACP entry");
        Check(File.ReadAllText(Path.Combine(terminalWorkspace, "fixture-terminal-authenticated")) == "terminal-test", "terminal login preserves configured entry cwd and passes args plus environment");
        Check(!File.Exists(Path.Combine(terminalWorkspace, "fixture-authenticated")), "terminal method never sent to authenticate");
    }
    string cancelledWorkspace = Path.Combine(root, "auth-cancel-workspace"); Directory.CreateDirectory(cancelledWorkspace);
    File.WriteAllText(Path.Combine(cancelledWorkspace, "fixture-auth-methods"), ""); File.WriteAllText(Path.Combine(cancelledWorkspace, "fixture-auth-cancel"), "");
    await host.OpenWorkspace(cancelledWorkspace);
    await Until(() => host.Status.StartsWith("需要登录") && !host.Busy, "cancelled authentication releases worker without claiming a connection");
    Check(!host.Connected && !File.Exists(host.Workspaces.SessionPath("opencode")), "cancelled login creates no false session binding");
    host.Configure("opencode", new(false, false, false));
    await Until(() => !host.Connected && server.Connections == 0, "disable closes owned ACP/MCP sessions");
}
finally { host?.Dispose(); Environment.SetEnvironmentVariable("PATH", oldPath); }
Console.WriteLine($"RESULT {passed} Agent checks passed; fixtures are not real model compatibility evidence.");

static class Fixture
{
    static string model = "fast", mode = "ask", thought = "low"; static bool permissionFlag;
    static object State(string? session = null)
    {
        if (File.Exists("fixture-legacy.flag")) return new {sessionId=session, models=new{currentModelId=model,availableModels=new[]{new{modelId="fast",name="Fast"},new{modelId="accurate",name="Accurate"}}},modes=new{currentModeId=mode,availableModes=new[]{new{id="ask",name="Ask"},new{id="code",name="Code"}}}};
        return new {sessionId=session, configOptions=new object[]{
            new{id="model",name="Model",category="model",type="select",currentValue=model,options=new[]{new{value="fast",name="Fast"},new{value="accurate",name="Accurate"}}},
            new{id="mode",name="Mode",category="mode",type="select",currentValue=mode,options=new[]{new{value="ask",name="Ask"},new{value="code",name="Code"}}},
            new{id="thought",name="Reasoning",category="thought_level",type="select",currentValue=thought,options=(model=="accurate"?new[]{"low","medium","high"}:new[]{"low"}).Select(v=>new{value=v,name=v}).ToArray()},
            new{id="permission_flag",name="Permission flag",category="mode",type="boolean",currentValue=permissionFlag}
        }};
    }
    static StreamReader? mcpReader;
    static StreamWriter? mcpWriter;
    static NamedPipeClientStream? mcp;
    static long rpc;
    static async Task Send(object value) { await Console.Out.WriteLineAsync(JsonSerializer.Serialize(value)); await Console.Out.FlushAsync(); }
    static async Task<object> Tool(string name, object args)
    {
        long id = ++rpc;
        await mcpWriter!.WriteLineAsync(JsonSerializer.Serialize(new { jsonrpc = "2.0", id, method = "tools/call", @params = new { name, arguments = args } }));
        while (true) { using var j = JsonDocument.Parse((await mcpReader!.ReadLineAsync())!); if (j.RootElement.TryGetProperty("id", out var i) && i.ToString() == id.ToString()) return j.RootElement.Clone(); }
    }
    static async Task Attach(JsonElement parameters)
    {
        mcp?.Dispose();
        string pipe = parameters.GetProperty("mcpServers")[0].GetProperty("args")[1].GetString()!;
        mcp = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly); await mcp.ConnectAsync(3000);
        mcpReader = new(mcp, Encoding.UTF8, false, 4096, true); mcpWriter = new(mcp, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
        await mcpWriter.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"id\":0,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-11-25\"}}"); await mcpReader.ReadLineAsync();
        await mcpWriter.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}");
    }
    public static async Task Run()
    {
        string session = ""; bool authenticated = false;
        while (await Console.In.ReadLineAsync() is string line)
        {
            using var json = JsonDocument.Parse(line); var msg = json.RootElement;
            if (!msg.TryGetProperty("method", out var method)) continue;
            var id = msg.TryGetProperty("id", out var i) ? i.Clone() : default;
            var p = msg.TryGetProperty("params", out var parameters) ? parameters : default;
            switch (method.GetString())
            {
                case "initialize": await Send(new { jsonrpc = "2.0", id, result = new { protocolVersion = 1, agentCapabilities = new { loadSession = true }, agentInfo = new { name = "Fixture", version = "1" }, authMethods = File.Exists("fixture-terminal-method") ? new object[] { new { id = "terminal-login", name = "Terminal", type = "terminal", args = new[] { "--fixture-login" }, env = new Dictionary<string,string> { ["FISH_LOGIN_TEST"] = "terminal-test" } } } : File.Exists("fixture-auth-methods") ? new object[] { new { id = "oauth-personal", name = "Google account" }, new { id = "future-login", name = "Future", type = "future" } } : [] } }); break;
                case "authenticate":
                    if (p.GetProperty("methodId").GetString() != "oauth-personal") throw new Exception("Unexpected authentication method");
                    authenticated = true; File.WriteAllText("fixture-authenticated", p.GetProperty("methodId").GetString());
                    await Send(new { jsonrpc = "2.0", id, result = new { } }); break;
                case "session/new":
                    if (File.Exists("fixture-terminal-method") && !File.Exists("fixture-terminal-authenticated")) { await Send(new { jsonrpc = "2.0", id, error = new { code = -32000, message = "Login required" } }); break; }
                    if (File.Exists("fixture-auth-methods") && !authenticated) { await Send(new { jsonrpc = "2.0", id, error = new { code = -32000, message = "Authentication required" } }); break; }
                    session = Guid.NewGuid().ToString("N"); File.WriteAllText("fixture-session.txt", session); await Attach(p); await Send(new { jsonrpc = "2.0", id, result = State(session) }); break;
                case "session/load":
                    if (File.Exists("fixture-auth-methods") && !authenticated) { await Send(new { jsonrpc = "2.0", id, error = new { code = -32000, message = "Authentication required" } }); break; }
                    if(File.Exists("fixture-auth-error")){await Send(new{jsonrpc="2.0",id,error=new{code=-32000,message="Authentication required"}});break;}
                    if(File.Exists("fixture-missing-session")){await Send(new{jsonrpc="2.0",id,error=new{code=-32002,message="Resource not found"}});break;}
                    session = p.GetProperty("sessionId").GetString()!; await Attach(p);
                    await Send(new { jsonrpc = "2.0", method = "session/update", @params = new { sessionId = session, update = new { sessionUpdate = "agent_message_chunk", content = new { type = "text", text = "replayed text must stay hidden" } } } });
                    await Send(new { jsonrpc = "2.0", id, result = State(session) }); break;
                case "session/set_config_option":
                    string config=p.GetProperty("configId").GetString()!;
                    if(config=="permission_flag") {if(p.GetProperty("type").GetString()!="boolean")throw new Exception("missing boolean discriminator");permissionFlag=p.GetProperty("value").GetBoolean();}
                    else {string value=p.GetProperty("value").GetString()!;if(config=="model")model=value;else if(config=="mode")mode=value;else thought=value;}
                    await Send(new{jsonrpc="2.0",id,result=State(session)});break;
                case "session/set_mode":
                    mode=p.GetProperty("modeId").GetString()!;
                    if(File.Exists("fixture-mode-override")){mode="ask";await Send(new{jsonrpc="2.0",method="session/update",@params=new{sessionId=session,update=new{sessionUpdate="current_mode_update",currentModeId=mode}}});}
                    await Send(new{jsonrpc="2.0",id,result=new{}});break;
                case "session/set_model": model=p.GetProperty("modelId").GetString()!;await Send(new{jsonrpc="2.0",id,result=new{}});break;
                case "session/prompt":
                    File.WriteAllText("fixture-last-prompt.txt", p.GetProperty("prompt")[0].GetProperty("text").GetString());
                    if (File.Exists("fixture-prompt-auth") && !File.Exists("fixture-prompt-auth-used"))
                    { File.WriteAllText("fixture-prompt-auth-used", ""); await Send(new {jsonrpc="2.0",id,error=new {code=-32000,message="Authentication required"}}); break; }
                    string text = p.GetProperty("prompt")[0].GetProperty("text").GetString()!;
                    if (text.Contains("permission"))
                    {
                        await Send(new { jsonrpc = "2.0", id = "permission-1", method = "session/request_permission", @params = new { sessionId = session, toolCall = new { title = "fixture permission", rawInput = new { command = "fixture" } }, options = new[] { new { optionId = "once", name = "允许一次", kind = "allow_once" } } } });
                        await Console.In.ReadLineAsync();
                    }
                    if (text.Contains("\"wait\"")) { await Console.In.ReadLineAsync(); await Send(new { jsonrpc = "2.0", id, result = new { stopReason = "cancelled" } }); break; }
                    await Tool("get_operation_guide", new { });
                    if (text.Contains("事件：start_care")) await Tool("schedule_wakeup", new { delay_seconds = 30, purpose = "fixture care" });
                    else await Tool("say", new { text = "tool-only response" });
                    await Send(new { jsonrpc = "2.0", method = "session/update", @params = new { sessionId = session, update = new { sessionUpdate = "agent_message_chunk", content = new { type = "text", text = "internal bookkeeping never visible" } } } });
                    await Send(new { jsonrpc = "2.0", id, result = new { stopReason = "end_turn" } }); break;
            }
        }
        mcp?.Dispose();
    }
}
