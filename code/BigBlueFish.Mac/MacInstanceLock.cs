namespace BigBlueFish.Windows;

internal static class MacInstanceLock
{
    internal static FileStream? TryAcquire()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home)) throw new IOException("无法确定 macOS 用户目录，不能确认应用实例锁。");
        // Keep the lock outside BIGBLUEFISH_DATA so alternate data roots still share it.
        var directory = Path.Combine(home, "Library", "Application Support", "BigBlueFish");
        Directory.CreateDirectory(directory);
        try { return new FileStream(Path.Combine(directory, "companion.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { return null; }
    }
}
