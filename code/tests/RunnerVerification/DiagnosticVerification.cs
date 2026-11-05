using BigBlueFish.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;

internal static class DiagnosticVerification
{
    static int passed;
    static void Check(bool value, string label) { if (!value) throw new Exception(label); passed++; Console.WriteLine("PASS " + label); }
    internal static int Run(string root)
    {
        root = Path.GetFullPath(root); Directory.CreateDirectory(root);
        const string secret = "PRIVATE-OCR-PROMPT-USER-APIKEY-987654";
        DiagnosticLog.ConfigureDirectories(root, Path.Combine(root, "data")); DiagnosticLog.Clear();
        DiagnosticLog.Write(DiagnosticEvent.ScreenReadFallback, kind: "ocr", error: new IOException(secret));
        DiagnosticLog.Write(DiagnosticEvent.AgentRequest, kind: secret, state: secret, action: secret, directory: Path.Combine(root, "data", "workspaces", secret), changed: ["Scale", secret]);
        DiagnosticLog.Write(DiagnosticEvent.AgentFailed, error: new HttpRequestException(secret, new Exception(secret), HttpStatusCode.Unauthorized));
        DiagnosticLog.Write(DiagnosticEvent.TrayState, error: new System.ComponentModel.Win32Exception(5, secret));
        DiagnosticLog.Write(DiagnosticEvent.AnimationStarted, kind: "Idle", action: "待机呼吸休闲");
        DiagnosticLog.ObserveSettings(new { Scale = 1, AgentHarnessId = secret, Chat = secret });
        var text = DiagnosticLog.ExportText();
        Check(!text.Contains(secret), "OCR/prompt/chat/key sentinel never appears in export");
        Check(text.Contains("kind=other") && text.Contains("state=other") && text.Contains("<unknown-action>"), "unknown free text is rejected by token/action whitelist");
        Check(text.Contains("directory=<Data>/workspaces/<private>"), "private workspace directory is anonymized");
        Check(text.Contains("changed=Scale") && !text.Contains("Chat"), "only known setting names, never values, are logged");
        Check(text.Contains("exception=IOException") && text.Contains("hresult=0x"), "exception type and error code retained without message");
        Check(text.Contains("http_status=401") && text.Contains("win32=5"), "HTTP and Win32 error codes remain useful");
        Check(text.Contains("action=待机呼吸休闲"), "public built-in action identifier is retained");
        Check(DiagnosticLog.DirectoryAlias(Path.Combine(root, "runtime")) == "<App>/runtime", "runtime directory has stable app alias");
        var external = DiagnosticLog.DirectoryAlias(Path.GetPathRoot(root)!);
        Check(external.StartsWith("<ExternalDirectory#") && external == DiagnosticLog.DirectoryAlias(Path.GetPathRoot(root)!), "external directory has same-run opaque identifier");
        var before = DiagnosticLog.Snapshot().Length;
        DiagnosticLog.ObserveSettings(new { Scale = 1, AgentHarnessId = secret, Chat = secret });
        Check(DiagnosticLog.Snapshot().Length == before, "unchanged settings do not flood the console");
        DiagnosticLog.ObserveSettings(new { Scale = 1.1, AgentHarnessId = secret, Chat = secret });
        Check(DiagnosticLog.Snapshot()[^1].Details == "changed=Scale", "setting changes are detected without serializing values into log");
        DiagnosticLog.Clear();
        Parallel.For(0, DiagnosticLog.Capacity + 5000, i => DiagnosticLog.Write(DiagnosticEvent.AnimationCompleted, count: i));
        var entries = DiagnosticLog.Snapshot();
        Check(entries.Length == DiagnosticLog.Capacity, $"rolling buffer retains exactly the latest {DiagnosticLog.Capacity} events");
        Check(entries.Zip(entries.Skip(1)).All(p => p.First.Sequence < p.Second.Sequence), "concurrent producers preserve strict event ordering");
        Check(entries[^1].Sequence - entries[0].Sequence == DiagnosticLog.Capacity - 1, "oldest records are evicted, no holes in retained sequence");
        var export = Path.Combine(root, "diagnostics.txt"); DiagnosticLog.SaveText(export);
        Check(File.ReadAllText(export) == DiagnosticLog.ExportText(), "TXT export exactly matches the current sanitized snapshot");
        Check(File.ReadAllBytes(export).Take(3).SequenceEqual(new byte[] { 239, 187, 191 }), "TXT includes UTF-8 BOM for Windows text editors");
        Check(File.ReadAllText(export).Contains($"Entries: {DiagnosticLog.Capacity}/{DiagnosticLog.Capacity}"), "export includes bounded count and environment header");
        DiagnosticLog.Clear();
        Console.WriteLine($"RESULT {passed} diagnostics checks passed"); return 0;
    }
}
