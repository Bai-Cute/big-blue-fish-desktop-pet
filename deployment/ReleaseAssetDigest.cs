using System;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BigBlueFish.Deployment;

internal static class ReleaseAssetDigest
{
    internal static string Sha256(JsonElement asset)
    {
        string digest = asset.TryGetProperty("digest", out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? "" : "";
        if (!Regex.IsMatch(digest, "^sha256:[a-fA-F0-9]{64}$", RegexOptions.IgnoreCase))
            throw new InvalidDataException("GitHub 尚未提供有效的安装包校验信息，请稍后重试。");
        return digest[7..].ToLowerInvariant();
    }
}
