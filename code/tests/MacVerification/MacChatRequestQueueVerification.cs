internal static class MacChatRequestQueueVerification
{
    static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception(label);
        Console.WriteLine("PASS: " + label);
    }

    internal static void Run()
    {
        var queue = new MacChatRequestQueue<string>();
        Check(queue.PendingCount == 0 && !queue.IsProcessing, "chat queue starts idle");
        Check(!queue.TryStartProcessing(), "empty chat queue does not start a processor");

        queue.Enqueue("first");
        Check(queue.TryStartProcessing(), "first request starts the queue processor");
        Check(!queue.TryStartProcessing(), "only one queue processor can run");
        Check(queue.TryDequeue(out var first) && first == "first", "processor claims the first request");
        queue.Enqueue("second");
        queue.Enqueue("third");
        Check(queue.PendingCount == 2, "requests can be queued while processing");

        Check(queue.TryDequeue(out var second) && second == "second", "second request follows the first");
        Check(queue.TryDequeue(out var third) && third == "third", "third request follows the second");
        Check(!queue.TryDequeue(out _), "dequeued requests leave no pending work");

        queue.Enqueue("discarded");
        queue.Clear();
        Check(queue.IsProcessing && queue.PendingCount == 0 && !queue.TryDequeue(out _), "clear drops queued requests without ending the active processor");
        queue.StopProcessing();
        Check(!queue.IsProcessing, "processor can return to idle");
        queue.Enqueue("after-clear");
        Check(queue.TryStartProcessing() && queue.TryDequeue(out var afterClear) && afterClear == "after-clear", "queue accepts new work after clear");
        queue.StopProcessing();
    }
}
