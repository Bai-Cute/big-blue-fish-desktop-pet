using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace BigBlueFish.Deployment;

internal sealed record AvailableUpdate(string Version, string Name, string Url, string Sha256, string Notes);

internal static class UpdateService
{
    internal const string LatestUrl = "https://api.github.com/repos/Bai-Cute/big-blue-fish-desktop-pet/releases/latest";
    internal static async Task<AvailableUpdate?> Check(string currentVersion, CancellationToken token = default, HttpClient? client = null, string? apiUrl = null)
    {
        using var owned = client == null ? new HttpClient { Timeout = TimeSpan.FromSeconds(20) } : null;
        var http = client ?? owned!;
        using var request = new HttpRequestMessage(HttpMethod.Get, apiUrl ?? LatestUrl);
        request.Headers.UserAgent.ParseAdd("BigBlueFish/" + typeof(UpdateService).Assembly.GetName().Version!.ToString(3));
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await http.SendAsync(request, token); response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        var release = json.RootElement;
        if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean()) return null;
        string version = release.GetProperty("tag_name").GetString()!.TrimStart('v');
        if (!System.Version.TryParse(version, out var next) || !System.Version.TryParse(currentVersion.Split('+')[0].Split('-')[0], out var current) || next <= current) return null;
#if COMPANION_SAFE
        string name = $"BigBlueFish-v{version}-Safe-OCR-Setup.exe";
#else
        string name = $"BigBlueFish-v{version}-OCR-Setup.exe";
#endif
        var assets = release.GetProperty("assets").EnumerateArray().ToArray();
        var installer = assets.FirstOrDefault(a => a.GetProperty("name").GetString() == name);
        if (installer.ValueKind == JsonValueKind.Undefined) throw new IOException("新版本的安装器暂未准备完成，请稍后重试。");
        string digest = installer.TryGetProperty("digest", out var value) ? value.GetString() ?? "" : "";
        string hash = digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? digest[7..] : "";
        if (!Regex.IsMatch(hash, "^[a-fA-F0-9]{64}$"))
        {
            var checksum = assets.FirstOrDefault(a => a.GetProperty("name").GetString() == name.Replace(".exe", ".sha256"));
            if (checksum.ValueKind == JsonValueKind.Undefined) throw new IOException("新版本的校验文件暂未准备完成。");
            using var checksumRequest = new HttpRequestMessage(HttpMethod.Get, checksum.GetProperty("browser_download_url").GetString());
            checksumRequest.Headers.UserAgent.ParseAdd("BigBlueFish/" + typeof(UpdateService).Assembly.GetName().Version!.ToString(3));
            using var checksumResponse = await http.SendAsync(checksumRequest, token); checksumResponse.EnsureSuccessStatusCode();
            var line = await checksumResponse.Content.ReadAsStringAsync(token);
            hash = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
            if (!Regex.IsMatch(hash, "^[a-fA-F0-9]{64}$")) throw new InvalidDataException("更新文件校验信息无效。");
        }
        return new AvailableUpdate(version, name, installer.GetProperty("browser_download_url").GetString()!, hash.ToLowerInvariant(),
            release.GetProperty("body").GetString() ?? "");
    }
}
