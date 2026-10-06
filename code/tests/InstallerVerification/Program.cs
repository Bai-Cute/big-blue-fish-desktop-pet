using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using BigBlueFish.Setup;

namespace BigBlueFish.InstallerVerification;

internal static class Checks
{
    static int count;
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        count++;
        Console.WriteLine("PASS " + message);
    }

    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            var target = Path.GetFullPath(args[1]);
            // Local developer deployment uses the very same extractor as the GUI installer.
            // This branch creates no test fixtures and never downloads or changes a model.
            if (args.Length == 3 && args[2] == "--apply-local-update")
            {
                using var payloadStream = InstallerForm.OpenPayload(args[0]);
                using var payloadArchive = new ZipArchive(payloadStream, ZipArchiveMode.Read);
                InstallerForm.ExtractApplication(payloadArchive, target);
                Console.WriteLine($"Installed {InstallerForm.ProductVersion} {InstallerForm.InputMode} to {target}");
                return 0;
            }
            Directory.CreateDirectory(target);
            var settings = Path.Combine(target, "preferences.json");
            if (!File.Exists(settings)) File.WriteAllText(settings, "{\"WeatherRegionCode\":\"340104\"}");
            Directory.CreateDirectory(Path.Combine(target, "models"));
            var retained = Path.Combine(target, "models", "retention-fixture.txt");
            if (!File.Exists(retained)) File.WriteAllText(retained, "retained-model-marker");
            var before = File.ReadAllBytes(settings);
            using var versionStream = typeof(Checks).Assembly.GetManifestResourceStream(typeof(Checks).Assembly.GetManifestResourceNames().Single(n => n.EndsWith("Version.props")))!;
            string expectedVersion = XDocument.Load(versionStream).Descendants("Version").Single().Value;
            Check(InstallerForm.ProductVersion == expectedVersion, "installer product version matches Version.props");
            using var stream = InstallerForm.OpenPayload(args[0]);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            var names = archive.Entries.Select(e => e.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var required in new[] { "VPet-Simulator.Windows.exe", "VPet-Simulator.Windows.dll",
                "VPet-Simulator.Core.dll", "VPet-Simulator.Windows.Interface.dll", "coreclr.dll", "hostfxr.dll",
                "PresentationFramework.dll", "runtime/llama-server.exe", "runtime/ggml-vulkan.dll", "runtime/mtmd.dll",
                "media/ffmpeg.exe", "media/FFmpeg-LICENSE.txt", "assets/fish/animations.json", "assets/fish/LICENSE.txt" })
                Check(names.Contains(required), "payload includes " + required);
            Check(names.Count(n => n.StartsWith("assets/fish/") && n.EndsWith(".webm")) == 106, "payload includes all 106 animations");
            Check(!names.Any(n => n.StartsWith("assets/pet/")), "old PNG animation pack is absent");
            Check(names.Contains("WinRT.Runtime.dll") == !InstallerForm.RequiresVision, "OCR projections match input mode");
            InstallerForm.ExtractApplication(archive, target);
            Check(File.ReadAllBytes(settings).SequenceEqual(before), "existing settings preserved");
            Check(File.ReadAllText(retained) == "retained-model-marker", "existing model directory preserved");
            foreach (var entry in archive.Entries.Where(e => e.Name.EndsWith(".dll") || e.Name.EndsWith(".exe")))
            {
                using var input = entry.Open();
                using var output = File.OpenRead(Path.Combine(target, entry.FullName));
                Check(SHA256.HashData(input).SequenceEqual(SHA256.HashData(output)), "extracted binary matches " + entry.FullName);
            }
            Check(File.Exists(Path.Combine(target, "WinRT.Runtime.dll")) == !InstallerForm.RequiresVision,
                "mode switch removes OCR-only projections");
            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(target, "installation-manifest.json")));
            Check(manifest.RootElement.GetProperty("Version").GetString() == expectedVersion, "installed manifest version matches");
            Check(manifest.RootElement.GetProperty("InputMode").GetString() == InstallerForm.InputMode,
                "installed manifest mode matches");
            using var runtime = JsonDocument.Parse(File.ReadAllText(Path.Combine(target, "VPet-Simulator.Windows.runtimeconfig.json")));
            Check(runtime.RootElement.GetProperty("runtimeOptions").TryGetProperty("includedFrameworks", out _),
                "application is self-contained");
            Console.WriteLine($"RESULT {count} installer checks passed ({InstallerForm.InputMode})");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
