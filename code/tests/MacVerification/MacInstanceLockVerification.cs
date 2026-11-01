using BigBlueFish.Windows;

internal static class MacInstanceLockVerification
{
    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "bbf-mac-lock-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "nested", "companion.lock");
            using (var first = MacInstanceLock.TryAcquire(path))
            {
                if (first == null) throw new Exception("The first test instance should acquire its isolated lock.");
                using var second = MacInstanceLock.TryAcquire(path);
                if (second != null) throw new Exception("A second test instance should not acquire the same lock.");
            }

            using var afterRelease = MacInstanceLock.TryAcquire(path);
            if (afterRelease == null) throw new Exception("The isolated lock should be available after release.");
            Console.WriteLine("PASS: macOS test instance lock is isolated and exclusive");
        }
        finally { Directory.Delete(directory, true); }
    }
}
