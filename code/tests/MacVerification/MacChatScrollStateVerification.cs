using BigBlueFish.Windows;

internal static class MacChatScrollStateVerification
{
    static void Check(bool value, string name)
    {
        if (!value) throw new Exception(name);
        Console.WriteLine("PASS: " + name);
    }

    internal static void Run()
    {
        Check(MacChatScrollState.IsAtBottom(200, 220, 0), "content fitting viewport is at bottom");
        Check(MacChatScrollState.IsAtBottom(1000, 500, 476), "near-bottom threshold follows latest");
        Check(!MacChatScrollState.IsAtBottom(1000, 500, 475), "scrolled-away position is not at bottom");

        var state = new MacChatScrollState();
        Check(state.OnMessageAdded(true) && state.UnreadCount == 0, "message at bottom follows without unread count");
        Check(!state.OnMessageAdded(false) && state.UnreadCount == 1, "message while reading history increments unread count");
        Check(!state.OnMessageAdded(false) && state.UnreadCount == 2, "multiple messages accumulate unread count");
        state.MarkLatestVisible();
        Check(state.UnreadCount == 0, "returning to latest clears unread count");

        Check(!state.OnMessageAdded(false) && state.UnreadCount == 1, "history position can become unread again");
        Check(state.OnMessageAdded(false, forceFollow: true) && state.UnreadCount == 0, "sending a message forces latest and clears unread count");
        Check(!state.OnMessageAdded(false) && state.UnreadCount == 1, "forced follow does not permanently override later history reading");
        state.Reset();
        Check(state.UnreadCount == 0, "reset clears unread count");
    }
}
