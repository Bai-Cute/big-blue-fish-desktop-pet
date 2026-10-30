using System.Text.Json;

namespace BigBlueFish.Agent;

public sealed record AgentAuthenticationMethod(string Id, string Name, string Description, string Type, string[] Arguments,
    Dictionary<string, string> Environment, bool ReplaceArguments = false)
{
    public static AgentAuthenticationMethod[] Parse(JsonElement container, bool terminalSupported)
    {
        if (container.ValueKind != JsonValueKind.Object || !container.TryGetProperty("authMethods", out var methods) || methods.ValueKind != JsonValueKind.Array) return [];
        var result = new List<AgentAuthenticationMethod>();
        static string Text(JsonElement value, string key, string fallback = "") => value.TryGetProperty(key, out var field) && field.ValueKind == JsonValueKind.String ? field.GetString() ?? fallback : fallback;
        foreach (var method in methods.EnumerateArray())
        {
            if (method.ValueKind != JsonValueKind.Object || Text(method, "id").Length == 0) continue;
            var source = method; bool legacy = false;
            if (method.TryGetProperty("_meta", out var meta) && meta.ValueKind == JsonValueKind.Object
                && meta.TryGetProperty("terminal-auth", out var terminal) && terminal.ValueKind == JsonValueKind.Object)
            { source = terminal; legacy = true; }
            string type = legacy ? "terminal" : Text(method, "type", "agent");
            if (type != "agent" && (type != "terminal" || !terminalSupported)) continue;
            string[] args = source.TryGetProperty("args", out var a) && a.ValueKind == JsonValueKind.Array
                ? a.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()!).ToArray() : [];
            var env = source.TryGetProperty("env", out var e) && e.ValueKind == JsonValueKind.Object
                ? e.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.String).ToDictionary(p => p.Name, p => p.Value.GetString()!) : [];
            result.Add(new(Text(method, "id"), Text(method, "name", "登录"), Text(method, "description"), type, args, env, legacy));
        }
        return result.DistinctBy(m => m.Id).ToArray();
    }
}
