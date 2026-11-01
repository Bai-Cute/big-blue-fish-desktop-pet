using BigBlueFish.Deployment;

namespace BigBlueFish.Windows;

internal sealed record MacModelProgress(long Received, long Total)
{
    internal double Percent => Total > 0 ? Math.Clamp(100d * Received / Total, 0, 100) : 0;
}

internal static class MacModelDownload
{
    // Keep the catalog, pinned revisions, filenames, and hashes in one place.
    internal static IReadOnlyList<ModelSource> Sources => ModelStore.Sources;

    internal static Task<string> DownloadAsync(string sourceId, string dataDirectory,
        IProgress<MacModelProgress>? progress = null, CancellationToken token = default, HttpClient? client = null)
    {
        var source = Sources.FirstOrDefault(x => x.Id == sourceId)
            ?? throw new ArgumentException("未知的模型下载来源。", nameof(sourceId));
        return DownloadAsync(source, dataDirectory, progress, token, client);
    }

    // An explicit source lets verification use a small local GGUF fixture. The UI only
    // calls the sourceId overload, which is restricted to the pinned source catalog.
    internal static async Task<string> DownloadAsync(ModelSource source, string dataDirectory,
        IProgress<MacModelProgress>? progress = null, CancellationToken token = default, HttpClient? client = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (string.IsNullOrWhiteSpace(dataDirectory)) throw new ArgumentException("模型目录不能为空。", nameof(dataDirectory));
        if (string.IsNullOrWhiteSpace(source.FileName) || Path.GetFileName(source.FileName) != source.FileName)
            throw new InvalidDataException("模型文件名无效。");
        var folder = Path.Combine(Path.GetFullPath(dataDirectory), "models");
        var target = Path.Combine(folder, source.FileName);
        // Keep a pre-existing, valid GGUF with a different hash untouched. The shared
        // downloader replaces the target after transfer, so reject this collision first.
        if (ModelStore.IsGguf(target) &&
            !string.Equals(await ModelStore.Hash(target, token), source.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("目标位置已有不同的 GGUF 模型，请先移走该文件。");
        var adapter = progress == null ? null : new Progress<ArtifactDownload.Progress>(p =>
            progress.Report(new MacModelProgress(p.Received, p.Total)));
        await ArtifactDownload.Download(source.Url, target, source.Sha256, adapter, token, client);
        token.ThrowIfCancellationRequested();
        if (!ModelStore.IsGguf(target))
        {
            // ArtifactDownload moves a SHA-matching file into place. A fixture can
            // match its hash yet have an invalid GGUF header; never leave that file
            // as the installed model. No previous valid target reaches this branch.
            File.Delete(target);
            throw new InvalidDataException("模型文件不是有效的 GGUF 或校验值不符。");
        }
        if (!string.Equals(await ModelStore.Hash(target, token), source.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("模型文件校验值不符，请重试。");
        ModelStore.Save(dataDirectory, new ModelStore.Selection(source.Id, source.FileName, source.Sha256));
        return target;
    }
}
