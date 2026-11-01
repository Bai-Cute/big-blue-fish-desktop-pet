using BigBlueFish.Windows;

internal static class MacBubbleReplyPolicyVerification
{
    static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception(label);
        Console.WriteLine("PASS: " + label);
    }

    internal static void Run()
    {
        var shortReply = "主人，鱼鱼今天也会陪着你。🐳";
        var shortDisplay = MacBubbleReplyPolicy.ForBubble(shortReply, 120, 100, 800);
        Check(shortDisplay == shortReply, "short bubble reply is displayed in full");

        var atLimit = new string('鲸', 200);
        Check(MacBubbleReplyPolicy.ForBubble(atLimit, 200, 300, 800) == atLimit,
            "reply at the configured threshold is displayed in full");

        var longReply = new string('鲸', 201);
        var longDisplay = MacBubbleReplyPolicy.ForBubble(longReply, 200, 300, 800);
        Check(longDisplay.Contains("聊天窗", StringComparison.Ordinal) && longDisplay != longReply,
            "reply over the configured threshold directs the user to the complete chat transcript");
        Check(!longDisplay.Contains(longReply[..80], StringComparison.Ordinal),
            "long bubble notice does not show a misleading truncated prefix");
        Check(MacBubbleReplyPolicy.ForBubble("短回复", 120, 400, 800).Contains("聊天窗", StringComparison.Ordinal),
            "bubble avoids clipping when its measured height exceeds the screen-safe limit");
        Check(MacBubbleReplyPolicy.ForBubble("🐳", 1, 100, 800) == "🐳",
            "character threshold counts graphemes instead of UTF-16 code units");
    }
}
