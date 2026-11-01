internal static class MacChatEntryVerification
{
    static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception(label);
        Console.WriteLine("PASS: " + label);
    }

    internal static void Run()
    {
        var firstUser = new MacChatEntry(true, "first question");
        var firstReply = new MacChatEntry(false, "正在思考…", isActivity: true);
        var secondUser = new MacChatEntry(true, "second question");
        var secondReply = new MacChatEntry(false, "排队中…", isActivity: true);
        var transcript = new List<MacChatEntry> { firstUser, firstReply, secondUser, secondReply };

        firstReply.SetResponse("first answer");
        Check(transcript[1] == firstReply && firstReply.Text == "first answer" && !firstReply.IsActivity,
            "completed reply replaces its thinking entry in place");
        secondReply.SetStatus("正在思考…");
        Check(transcript[3] == secondReply && secondReply.Text == "正在思考…" && secondReply.IsActivity,
            "queued entry changes to thinking without moving");
        secondReply.SetResponse("second answer");
        Check(transcript[2] == secondUser && transcript[3].Text == "second answer",
            "queued answers stay paired with their sent messages");
    }
}
