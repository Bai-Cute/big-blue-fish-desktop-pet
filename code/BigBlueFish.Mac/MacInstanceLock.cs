namespace BigBlueFish.Windows;

internal static class MacInstanceLock
{
    internal static FileStream? TryAcquire(string? lockPath = null)
    {
        if (string.IsNullOrWhiteSpace(lockPath))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(home)) throw new IOException("无法确定 macOS 用户目录，不能确认应用实例锁。");
            // Keep the production lock outside BIGBLUEFISH_DATA so alternate data roots still share it.
            var directory = Path.Combine(home, "Library", "Application Support", "BigBlueFish");
            lockPath = Path.Combine(directory, "companion.lock");
        }
        else lockPath = Path.GetFullPath(lockPath);

        var lockDirectory = Path.GetDirectoryName(lockPath);
        if (string.IsNullOrEmpty(lockDirectory)) throw new IOException("无法确定实例锁目录。");
        Directory.CreateDirectory(lockDirectory);

        try { return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { return null; }
    }
}
