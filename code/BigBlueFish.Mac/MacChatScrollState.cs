namespace BigBlueFish.Windows;

internal sealed class MacChatScrollState
{
    internal int UnreadCount { get; private set; }

    internal bool OnMessageAdded(bool wasAtBottom, bool forceFollow = false)
    {
        if (forceFollow || wasAtBottom) {
            UnreadCount = 0;
            return true;
        }

        UnreadCount++;
        return false;
    }

    internal void MarkLatestVisible() => UnreadCount = 0;

    internal void Reset() => UnreadCount = 0;

    internal static bool IsAtBottom(double extentHeight, double viewportHeight, double offsetY, double threshold = 24)
    {
        if (!double.IsFinite(extentHeight) || !double.IsFinite(viewportHeight) ||
            !double.IsFinite(offsetY) || !double.IsFinite(threshold)) return false;

        extentHeight = Math.Max(0, extentHeight);
        viewportHeight = Math.Max(0, viewportHeight);
        offsetY = Math.Max(0, offsetY);
        threshold = Math.Max(0, threshold);
        return extentHeight <= viewportHeight || extentHeight - viewportHeight - offsetY <= threshold;
    }
}
