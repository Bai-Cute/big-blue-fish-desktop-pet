using System.Text.Json;
using BigBlueFish.Agent;

/// <summary>Live probe for the harness that ships inside the installed DeepSeek Harness desktop app.</summary>
static class DshDesktopProbe
{
    public static async Task<int> Run(string root)
    {
        root = Path.GetFullPath(root); Directory.CreateDirectory(root);
        var harness = HarnessCatalog.Get("dsh-desktop");
        var adapters = new AgentAdapterStore(root);
        string? executable = adapters.FindExecutable(harness);
        if (executable == null)
        {
            Console.WriteLine("RESULT " + HarnessDesktopLocator.NotFoundMessage(harness));
            return 2;
        }
        var command = await adapters.Resolve(harness, executable, CancellationToken.None, Console.WriteLine);
        var start = AgentProcess.StartInfo(command.Path, command.Arguments, root);
        start.Environment["ELECTRON_RUN_AS_NODE"] = "1";
        Console.WriteLine("EXECUTABLE " + command.Path);
        Console.WriteLine("ARGUMENTS " + JsonSerializer.Serialize(command.Arguments));
        await using var client = new AcpConnection(start, (_, _) => Task.FromResult<string?>(null));
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await client.Initialize(deadline.Token);
        Console.WriteLine("INIT " + JsonSerializer.Serialize(new { agent = client.AgentName, version = client.AgentVersion, capabilities = client.Capabilities }));
        var created = await client.Request("session/new", new { cwd = root, mcpServers = Array.Empty<object>() }, deadline.Token);
        string sessionId = created.GetProperty("sessionId").GetString() ?? "";
        Console.WriteLine("SESSION " + sessionId);
        if (created.TryGetProperty("configOptions", out var options))
            Console.WriteLine("CONFIG " + JsonSerializer.Serialize(options));
        await client.Request("session/close", new { sessionId }, deadline.Token);
        Console.WriteLine("RESULT desktop runtime served ACP: initialize, session/new and session/close all answered");
        return 0;
    }
}
