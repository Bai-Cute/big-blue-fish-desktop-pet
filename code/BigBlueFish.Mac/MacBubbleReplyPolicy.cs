using System.Globalization;

namespace BigBlueFish.Windows;

internal static class MacBubbleReplyPolicy
{
    internal const double MaximumBubbleHeight = 320;
    internal const double MaximumWorkAreaFraction = .42;
    internal const string LongReplyNotice = "这条回复比较长，完整内容请查看聊天窗。";

    internal static string ForBubble(string response, int characterLimit, double measuredBubbleHeight, double workAreaHeight)
    {
        if (StringInfo.ParseCombiningCharacters(response).Length > Math.Max(0, characterLimit))
            return LongReplyNotice;
        if (!double.IsFinite(measuredBubbleHeight) || !double.IsFinite(workAreaHeight) || workAreaHeight <= 0)
            return LongReplyNotice;

        double limit = Math.Min(MaximumBubbleHeight, workAreaHeight * MaximumWorkAreaFraction);
        return measuredBubbleHeight <= limit ? response : LongReplyNotice;
    }
}
