using System.Runtime.InteropServices;
using System.Text;

namespace BigBlueFish.Windows;

internal static class MacApiKeyStore
{
    const int ItemNotFound = -25300;

    internal static string? Read()
    {
        var status = bbf_keychain_read(out var value);
        if (status == ItemNotFound) return null;
        EnsureSuccess(status, "读取");
        try { return Marshal.PtrToStringUTF8(value); }
        finally { bbf_keychain_free(value); }
    }

    internal static void Save(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) { Delete(); return; }
        var utf8 = Encoding.UTF8.GetBytes(value + "\0");
        try { EnsureSuccess(bbf_keychain_write(utf8), "保存"); }
        finally { Array.Clear(utf8); }
    }

    internal static void Delete() => EnsureSuccess(bbf_keychain_delete(), "删除");

    static void EnsureSuccess(int status, string operation)
    {
        if (status != 0) throw new IOException($"无法从 macOS 钥匙串{operation} API Key（状态码 {status}）。");
    }

    [DllImport("libBigBlueFish.Mac.dylib")] static extern int bbf_keychain_read(out IntPtr value);
    [DllImport("libBigBlueFish.Mac.dylib")] static extern int bbf_keychain_write(byte[] value);
    [DllImport("libBigBlueFish.Mac.dylib")] static extern int bbf_keychain_delete();
    [DllImport("libBigBlueFish.Mac.dylib")] static extern void bbf_keychain_free(IntPtr value);
}
