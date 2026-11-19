using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace BigBlueFish.Agent;

/// <summary>
/// Reads the file index of an Electron app.asar archive without Electron. A plain .NET process sees the
/// archive as one file, so the header is parsed to answer whether a bundled path exists.
/// </summary>
sealed class AsarArchive : IDisposable
{
    const int MaxHeaderBytes = 32 * 1024 * 1024;

    readonly JsonDocument header;

    AsarArchive(JsonDocument header) => this.header = header;

    /// <summary>Open the archive header; returns null when the file is absent or not an asar archive.</summary>
    public static AsarArchive? Open(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var sizes = new byte[8];
            ReadExactly(stream, sizes);
            uint headerSize = BinaryPrimitives.ReadUInt32LittleEndian(sizes.AsSpan(4));
            if (headerSize is < 16 or > MaxHeaderBytes) return null;
            var header = new byte[headerSize];
            ReadExactly(stream, header);
            // The header is a pickle: payload size, string length, then the JSON text.
            uint jsonLength = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4));
            if (jsonLength == 0 || jsonLength > headerSize - 8) return null;
            return new AsarArchive(JsonDocument.Parse(Encoding.UTF8.GetString(header, 8, (int)jsonLength)));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>True when the archive index lists this slash-separated entry as a file.</summary>
    public bool Contains(string entry)
    {
        JsonElement files = header.RootElement;
        foreach (string segment in entry.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (files.ValueKind != JsonValueKind.Object) return false;
            if (!files.TryGetProperty("files", out files) || files.ValueKind != JsonValueKind.Object) return false;
            if (!files.TryGetProperty(segment, out files)) return false;
        }
        return files.ValueKind == JsonValueKind.Object && files.TryGetProperty("size", out var size)
            && size.ValueKind == JsonValueKind.Number && size.TryGetInt64(out long bytes) && bytes >= 0;
    }

    public void Dispose() => header.Dispose();

    static void ReadExactly(Stream stream, byte[] buffer)
    {
        int read = 0;
        while (read < buffer.Length)
        {
            int count = stream.Read(buffer, read, buffer.Length - read);
            if (count == 0) throw new IOException("asar 头部不完整。");
            read += count;
        }
    }
}
