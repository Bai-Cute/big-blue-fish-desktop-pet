internal sealed class MacChatRequestQueue<T>
{
    readonly Queue<T> pending = new();

    internal int PendingCount => pending.Count;
    internal bool IsProcessing { get; private set; }

    internal void Enqueue(T request) => pending.Enqueue(request);

    internal bool TryStartProcessing()
    {
        if (IsProcessing || pending.Count == 0) return false;
        IsProcessing = true;
        return true;
    }

    internal bool TryDequeue(out T request)
    {
        if (pending.TryDequeue(out request!)) return true;
        request = default!;
        return false;
    }

    internal void StopProcessing() => IsProcessing = false;
    internal void Clear() => pending.Clear();
}
