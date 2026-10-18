using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace BigBlueFish.Deployment;

internal static class ArtifactDownload
{
    internal sealed record Progress(long Received, long Total)
    {
        internal double Percent => Total > 0 ? Math.Clamp(100d * Received / Total, 0, 100) : 0;
        public override string ToString() => Total > 0
            ? $"{Received / 1048576d:0.0} / {Total / 1048576d:0.0} MB · {Percent:0}%"
            : $"已下载 {Received / 1048576d:0.0} MB";
    }

    internal static async Task Download(string url, string destination, string expectedHash,
        IProgress<Progress>? progress = null, CancellationToken token = default, HttpClient? client = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (File.Exists(destination) && await ModelStore.Hash(destination, token) == expectedHash) return;
        var partial = destination + ".partial";
        long offset = File.Exists(partial) ? new FileInfo(partial).Length : 0;
        using var ownedClient = client == null ? new HttpClient(new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(20) })
            { Timeout = TimeSpan.FromHours(2) } : null;
        var http = client ?? ownedClient!;
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("BigBlueFish/" + typeof(ArtifactDownload).Assembly.GetName().Version!.ToString(3));
        if (offset > 0) request.Headers.Range = new RangeHeaderValue(offset, null);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            if (await ModelStore.Hash(partial, token) == expectedHash)
            { File.Move(partial, destination, true); return; }
            File.Delete(partial);
            throw new IOException("未完成的下载需要重新开始，请重试。");
        }
        response.EnsureSuccessStatusCode();
        bool append = offset > 0 && response.StatusCode == HttpStatusCode.PartialContent
            && response.Content.Headers.ContentRange?.From == offset;
        if (!append) offset = 0;
        long total = response.Content.Headers.ContentRange?.Length ?? (response.Content.Headers.ContentLength ?? 0) + offset;
        await using (var input = await response.Content.ReadAsStreamAsync(token))
        await using (var output = new FileStream(partial, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.Read, 1048576, true))
        {
            var buffer = new byte[1048576]; long received = offset;
            while (true)
            {
                using var idle = CancellationTokenSource.CreateLinkedTokenSource(token);
                idle.CancelAfter(TimeSpan.FromSeconds(45));
                int read;
                try { read = await input.ReadAsync(buffer, idle.Token); }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                { throw new IOException("下载连接暂时没有响应。"); }
                if (read == 0) break;
                await output.WriteAsync(buffer.AsMemory(0, read), token);
                received += read; progress?.Report(new Progress(received, total));
            }
        }
        if (total > 0 && new FileInfo(partial).Length < total)
            throw new IOException("下载连接提前结束，已有进度已保留，请重试。");
        if (await ModelStore.Hash(partial, token) != expectedHash)
        {
            File.Delete(partial);
            throw new InvalidDataException("下载文件校验未通过，请重试。");
        }
        File.Move(partial, destination, true);
    }
}
