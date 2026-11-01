internal sealed class MacChatEntry(bool isUser, string text, bool isActivity = false)
{
    internal bool IsUser { get; } = isUser;
    internal string Text { get; private set; } = text;
    internal bool IsActivity { get; private set; } = isActivity;

    internal void SetStatus(string text)
    {
        Text = text;
        IsActivity = true;
    }

    internal void SetResponse(string text)
    {
        Text = text;
        IsActivity = false;
    }
}
