using System.Text.Json;

namespace BigBlueFish.Agent;

public sealed record AgentConfigChoice(string Value, string Name, string Description = "", string Group = "");
public sealed record AgentConfigOption(string Id, string Name, string Description, string Category, string Type,
    string CurrentValue, AgentConfigChoice[] Choices, string Method = "session/set_config_option");

/// <summary>Selectors mirror Agent-advertised values; unsupported controls are never synthesized.</summary>
public sealed class AgentSessionConfiguration
{
    private readonly object gate = new();
    private AgentConfigOption[] options = [];
    private bool modern;
    public int Revision { get; private set; }
    public AgentConfigOption[] Options { get { lock (gate) return options.ToArray(); } }
    public string Model => Options.FirstOrDefault(o => o.Category == "model")?.CurrentValue ?? "由 Harness 配置";
    public void Reset() { lock (gate) { options = []; modern = false; Revision++; } }
    private static string Text(JsonElement obj, string key, string fallback = "") => obj.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? fallback : fallback;
    public void Read(JsonElement data)
    {
        lock (gate)
        {
            if (data.TryGetProperty("configOptions", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                modern = true; var found = new List<AgentConfigOption>();
                foreach (var item in list.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object) continue;
                    string id = Text(item, "id"), type = Text(item, "type");
                    if (id.Length == 0 || type is not ("select" or "boolean") || !item.TryGetProperty("currentValue", out var current)) continue;
                    if (type == "boolean" && current.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
                        || type == "select" && current.ValueKind != JsonValueKind.String) continue;
                    var choices = new List<AgentConfigChoice>();
                    if (type == "select" && item.TryGetProperty("options", out var values) && values.ValueKind == JsonValueKind.Array)
                        foreach (var value in values.EnumerateArray())
                        {
                            if (value.ValueKind != JsonValueKind.Object) continue;
                            if (value.TryGetProperty("options", out var nested) && nested.ValueKind == JsonValueKind.Array)
                                foreach (var entry in nested.EnumerateArray()) AddChoice(entry, Text(value, "name"), choices);
                            else AddChoice(value, "", choices);
                        }
                    found.Add(new(id, Text(item, "name", id), Text(item, "description"), Text(item, "category"), type,
                        type == "boolean" ? current.GetBoolean().ToString().ToLowerInvariant() : current.GetString()!, choices.ToArray()));
                }
                options = found.DistinctBy(o => o.Id).ToArray(); Revision++; return;
            }
            if (modern) return;
            var legacy = options.ToList();
            void Legacy(string field, string currentKey, string listKey, string category, string method)
            {
                if (!data.TryGetProperty(field, out var state) || state.ValueKind != JsonValueKind.Object) return;
                string current = Text(state, currentKey), id = "$legacy-" + category;
                var choices = new List<AgentConfigChoice>();
                if (state.TryGetProperty(listKey, out var values) && values.ValueKind == JsonValueKind.Array)
                    foreach (var item in values.EnumerateArray())
                    {
                        if (item.ValueKind != JsonValueKind.Object) continue;
                        string key = Text(item, category == "model" ? "modelId" : "id");
                        if (key.Length > 0) choices.Add(new(key, Text(item, "name", key), Text(item, "description")));
                    }
                legacy.RemoveAll(o => o.Id == id);
                if (current.Length > 0 || choices.Count > 0) legacy.Add(new(id, category == "model" ? "模型" : "权限／工作模式", "由当前 Harness 提供", category, "select", current, choices.ToArray(), method));
            }
            Legacy("models", "currentModelId", "availableModels", "model", "session/set_model");
            Legacy("modes", "currentModeId", "availableModes", "mode", "session/set_mode");
            options = legacy.ToArray(); Revision++;
        }
    }
    private static void AddChoice(JsonElement item, string group, List<AgentConfigChoice> choices)
    {
        if (item.ValueKind != JsonValueKind.Object) return;
        string value = Text(item, "value"); if (value.Length > 0) choices.Add(new(value, Text(item, "name", value), Text(item, "description"), group));
    }
    public void Notification(JsonElement update)
    {
        string kind = Text(update, "sessionUpdate");
        if (kind == "config_option_update") Read(update);
        else if (kind == "current_mode_update")
        {
            lock (gate)
            {
                if (modern) return;
                string value = Text(update, "currentModeId");
                options = options.Select(o => o.Category == "mode" ? o with { CurrentValue = value } : o).ToArray(); Revision++;
            }
        }
    }
    public async Task Apply(AcpConnection client, string sessionId, string id, string value, CancellationToken token)
    {
        AgentConfigOption option = Options.FirstOrDefault(o => o.Id == id) ?? throw new ArgumentException("Agent 已不提供此配置项，请刷新后重试。");
        if (option.Method.Length == 0) throw new InvalidOperationException("此选项由 Harness 配置。");
        if (option.Type == "boolean" ? value is not ("true" or "false") : !option.Choices.Any(c => c.Value == value)) throw new ArgumentException("请选择 Agent 提供的有效选项。");
        object args = option.Method switch {
            "session/set_mode" => new { sessionId, modeId = value },
            "session/set_model" => new { sessionId, modelId = value },
            _ when option.Type == "boolean" => new { sessionId, configId = id, type = "boolean", value = value == "true" },
            _ => new { sessionId, configId = id, value }
        };
        int before = Revision;
        JsonElement response;
        try { response = await client.Request(option.Method, args, token); }
        catch (AcpException error) when (error.Code == -32601)
        {
            lock (gate) { options = options.Select(o => o.Id == id ? o with { Method = "" } : o).ToArray(); Revision++; }
            throw new IOException("Agent 未实现此配置的切换方法，请在对应 Harness 中配置。", error);
        }
        token.ThrowIfCancellationRequested();
        if (option.Method == "session/set_config_option")
        {
            if (!response.TryGetProperty("configOptions", out var array) || array.ValueKind != JsonValueKind.Array) throw new IOException("Agent 未返回修改后的配置状态。");
            if (Revision == before) Read(response);
        }
        else
        {
            lock (gate) if (Revision == before)
            {
                if (response.TryGetProperty("modes", out _) || response.TryGetProperty("models", out _)) Read(response);
                else { options = options.Select(o => o.Id == id ? o with { CurrentValue = value } : o).ToArray(); Revision++; }
            }
        }
    }
}
