using System.Text.Json;
using System.Text.Json.Serialization;
using BigBlueFish.Agent;

namespace BigBlueFish.Windows;

internal sealed class MacChatSettings
{
    internal const int DefaultBubbleCharacterLimit = 120;
    internal const int MinimumBubbleCharacterLimit = 20;
    internal const int MaximumBubbleCharacterLimit = 1000;
    internal const string DefaultPersonalityPrompt = MacChatPersonality.DefaultPrompt;
    internal const string PreviousDefaultPersonalityPrompt = """
你是“蓝色大肥鱼”的桌面伙伴，昵称“大肥鱼”或“鱼鱼”。语气温柔、亲切、自然，认真回应用户，不夸张、不刻意卖萌。优先使用中文，回答清楚简洁。
用户问你是谁时，自称大肥鱼（蓝色大肥鱼桌宠）；不要主动自称 ChatGPT、OpenAI、Claude、Codex 或其他厂商/模型。若用户明确询问底层模型或服务来源，请说明回复由当前配置的 API、本机模型或 ACP Agent 提供，不要编造。
只依据当前对话和实际可用能力回答；不要声称能看见屏幕、记住未提供的事情、具有真实感受或已经执行未执行的动作。
""";

    public string Backend { get; set; } = "api";
    public string AcpProvider { get; set; } = "opencode";
    public string AcpExecutable { get; set; } = "";
    public string Endpoint { get; set; } = "https://api.deepseek.com";
    public string Model { get; set; } = "deepseek-flash";
    public string ApiProtocol { get; set; } = "chat";
    public string LlamaServerExecutable { get; set; } = "";
    public string GgufPath { get; set; } = "";
    public bool AcpConsent { get; set; }
    public double WindowOpacity { get; set; } = .94;
    public int BubbleCharacterLimit { get; set; } = DefaultBubbleCharacterLimit;
    public string PersonalityPrompt { get; set; } = DefaultPersonalityPrompt;

    [JsonIgnore]
    internal string FilePath => Path.Combine(Environment.CurrentDirectory, "mac-chat-settings.json");

    internal static MacChatSettings Read()
    {
        foreach (var path in new[] { "mac-chat-settings.json", "mac-chat-settings.json.bak" })
        {
            try
            {
                var value = JsonSerializer.Deserialize<MacChatSettings>(File.ReadAllText(path));
                if (value != null) return value.Normalize();
            }
            catch (JsonException) { }
            catch (IOException) { }
        }
        return new MacChatSettings();
    }

    internal void Save()
    {
        Normalize();
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, json);
        if (File.Exists(FilePath)) File.Copy(FilePath, FilePath + ".bak", true);
        File.Move(temp, FilePath, true);
    }

    internal MacChatSettings Normalize()
    {
        Backend = (Backend ?? "").Trim().ToLowerInvariant() switch { "acp" => "acp", "gguf" => "gguf", _ => "api" };
        var provider = (AcpProvider ?? "").Trim().ToLowerInvariant();
        if (!HarnessCatalog.All.Any(h => h.Id == provider)) { AcpExecutable = ""; AcpConsent = false; }
        AcpProvider = HarnessCatalog.All.Any(h => h.Id == provider) ? provider : "opencode";
        AcpExecutable = (AcpExecutable ?? "").Trim();
        Endpoint = string.IsNullOrWhiteSpace(Endpoint) ? "https://api.deepseek.com" : Endpoint.Trim();
        Model = string.IsNullOrWhiteSpace(Model) ? "deepseek-flash" : Model.Trim();
        ApiProtocol = (ApiProtocol ?? "").Trim().ToLowerInvariant() == "responses" ? "responses" : "chat";
        LlamaServerExecutable = (LlamaServerExecutable ?? "").Trim();
        GgufPath = (GgufPath ?? "").Trim();
        PersonalityPrompt = (PersonalityPrompt ?? "").Trim();
        if (PersonalityPrompt == PreviousDefaultPersonalityPrompt) PersonalityPrompt = DefaultPersonalityPrompt;
        if (PersonalityPrompt.Length > 4000) PersonalityPrompt = PersonalityPrompt[..4000].TrimEnd();
        WindowOpacity = double.IsFinite(WindowOpacity) ? Math.Clamp(WindowOpacity, .65, 1) : .94;
        BubbleCharacterLimit = Math.Clamp(BubbleCharacterLimit, MinimumBubbleCharacterLimit, MaximumBubbleCharacterLimit);
        return this;
    }

    internal bool ChangesAcpConnection(MacChatSettings next) =>
        (Backend == "acp" || next.Backend == "acp") &&
        (Backend != next.Backend || AcpProvider != next.AcpProvider || AcpExecutable != next.AcpExecutable ||
        AcpConsent != next.AcpConsent);
}
