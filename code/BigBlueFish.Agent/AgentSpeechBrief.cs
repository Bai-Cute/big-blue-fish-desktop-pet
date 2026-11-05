namespace BigBlueFish.Agent;

public sealed record AgentPromptImage(string MimeType, string Data)
{
    public static AgentPromptImage FromDataUrl(string value)
    {
        int comma = value.IndexOf(',');
        if (!value.StartsWith("data:image/", StringComparison.Ordinal) || comma < 0
            || !value[..comma].EndsWith(";base64", StringComparison.Ordinal))
            throw new InvalidDataException("前台截图编码无效。");
        return new(value[5..value.IndexOf(';')], value[(comma + 1)..]);
    }
}

public sealed record AgentSpeechBrief(string Text, AgentPromptImage? Image = null);
