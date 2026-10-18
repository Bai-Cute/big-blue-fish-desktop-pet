using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BigBlueFish.Deployment;

internal sealed record ModelSource(string Id, string Label, string FileName, string Url, string Page, string Sha256);

internal static class ModelStore
{
    internal const string SelectionName = "model-selection.json";
    internal static readonly ModelSource HuggingFace = new("huggingface", "Hugging Face（推荐）",
        "Qwen3.5-4B-heretic-Q4_K_M.gguf",
        "https://huggingface.co/Biomanticus/Qwen3.5-4B-heretic-gguf/resolve/b63ff4662e4863cfa005c9cd8ed34b87ed44b7e1/Qwen3.5-4B-heretic-f16_Q4_K_M.gguf?download=true",
        "https://huggingface.co/Biomanticus/Qwen3.5-4B-heretic-gguf/tree/b63ff4662e4863cfa005c9cd8ed34b87ed44b7e1",
        "8485535a36c9f333574d08b650ad698ac02ec30752bd9cd87e493a3b7531bee1");
    internal static readonly ModelSource Domestic = new("modelscope", "国内源（稳定）",
        "qwen3-5-4B-Q4_K_M.gguf",
        "https://modelscope.cn/models/diodel/Qwen3.5-4B-Q4_K_M-GGUF/resolve/124e4a76470ea89369ecddc493a8c1d75e3c9498/qwen3-5-4B-Q4_K_M.gguf",
        "https://modelscope.cn/models/diodel/Qwen3.5-4B-Q4_K_M-GGUF/files",
        "de8e96cd0d0c358487091aaaed1346bc02e61da3d4b412c833662702e233e78c");
    internal static readonly ModelSource[] Sources = [HuggingFace, Domestic];
    internal sealed record Selection(string Source, string? FileName, string? Sha256);

    internal static Selection? ReadSelection(string directory)
    {
        try { return JsonSerializer.Deserialize<Selection>(File.ReadAllText(Path.Combine(directory, SelectionName))); }
        catch { return null; }
    }

    internal static string? Resolve(string directory)
    {
        var selection = ReadSelection(directory);
        if (selection?.Source == "none") return null;
        if (selection?.FileName is { } selected)
        {
            // Selected files are imported into models; preserve their actual filename.
            if (Path.GetFileName(selected) != selected) return null;
            var path = Path.Combine(directory, "models", selected);
            return IsGguf(path) ? path : null;
        }
        foreach (var name in new[] { HuggingFace.FileName, "Qwen3.5-4B-heretic-f16_Q4_K_M.gguf", Domestic.FileName })
        {
            var path = Path.Combine(directory, "models", name);
            if (IsGguf(path)) return path;
        }
        return null;
    }

    internal static bool IsGguf(string path)
    {
        try
        {
            using var input = File.OpenRead(path);
            if (input.Length < 24) return false;
            using var reader = new BinaryReader(input);
            return reader.ReadUInt32() == 0x46554747 && reader.ReadUInt32() > 0
                && reader.ReadUInt64() > 0 && reader.ReadUInt64() > 0;
        }
        catch { return false; }
    }

    internal static void Save(string directory, Selection selection)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, SelectionName);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(selection));
        File.Move(path + ".tmp", path, true);
    }

    internal static async Task<string> Hash(string path, CancellationToken token = default)
    {
        await using var input = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(input, token)).ToLowerInvariant();
    }

    internal static bool Expected(string hash) => Sources.Any(s => hash.Equals(s.Sha256, StringComparison.OrdinalIgnoreCase));

    internal static async Task Import(string directory, string sourcePath, string hash, CancellationToken token = default)
    {
        if (!IsGguf(sourcePath)) throw new InvalidDataException("请选择有效的 GGUF 模型文件。");
        var folder = Path.Combine(directory, "models"); Directory.CreateDirectory(folder);
        var name = Path.GetFileName(sourcePath);
        var destination = Path.Combine(folder, name);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!Path.GetFullPath(sourcePath).Equals(Path.GetFullPath(destination), comparison))
        {
            var partial = destination + ".importing";
            await using (var source = File.OpenRead(sourcePath))
            await using (var output = File.Create(partial)) await source.CopyToAsync(output, token);
            if (!IsGguf(partial) || await Hash(partial, token) != hash)
                throw new InvalidDataException("模型文件复制未完成，请重试。");
            File.Move(partial, destination, true);
        }
        var known = Sources.FirstOrDefault(s => s.Sha256.Equals(hash, StringComparison.OrdinalIgnoreCase));
        Save(directory, new Selection(known?.Id ?? "manual", name, hash));
    }
}
