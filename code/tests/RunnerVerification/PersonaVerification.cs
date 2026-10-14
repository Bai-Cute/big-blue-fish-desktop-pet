using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using BigBlueFish.Windows;

internal static class PersonaVerification
{
    internal sealed record Scene(string Id, string Kind, string Context, int Repeats = 2);
#if COMPANION_OCR
    internal static async Task<int> ActualScreen(string root, string outputFile)
    {
        Environment.CurrentDirectory = Path.GetFullPath(root);
        var handle = CompanionNative.GetForegroundWindow();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(100));
        var snapshot = await CompanionOcr.ReadAsync(handle, cancellation.Token);
        var context = $"现在是{DateTime.Now:yyyy年M月d日 HH:mm}。\n" + snapshot.Context;
        using var brain = new CompanionBrain { StopOnBattery = false };
        await brain.WarmUp();
        var complete = typeof(CompanionBrain).GetMethod("Complete", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var outputs = new List<string>();
        for (int i = 0; i < 3; i++)
        {
            string request = CompanionBrain.OcrSpeechRequest(context);
            var text = await (Task<string>)complete.Invoke(brain, [CompanionBrain.OcrPersona, request, CompanionDialogue.Temperature,
                cancellation.Token, null, null])!;
            outputs.Add(text);
            Console.WriteLine("REAL FOREGROUND " + text);
        }
        File.WriteAllText(outputFile, JsonSerializer.Serialize(new { actualForeground = true, handle = handle.ToInt64(),
            snapshot.Context, snapshot.WordCount, snapshot.Width, snapshot.Height, outputs }, new JsonSerializerOptions { WriteIndented = true }));
        brain.Stop();
        return 0;
    }
#endif
    internal static async Task<int> Run(string root, string fixtureFile, string outputFile)
    {
        Environment.CurrentDirectory = Path.GetFullPath(root);
        var scenes = JsonSerializer.Deserialize<Scene[]>(File.ReadAllText(fixtureFile))!;
        using var brain = new CompanionBrain { StopOnBattery = false };
        await brain.WarmUp();
        if (brain.RunnerPid == null) throw new Exception("Actual model did not load: " + brain.Status);
        var complete = typeof(CompanionBrain).GetMethod("Complete", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var records = new List<object>();
        foreach (var scene in scenes)
        for (int run = 1; run <= scene.Repeats; run++)
        {
            string persona = scene.Kind switch { "Weather" => CompanionBrain.WeatherPersona, "News" => CompanionBrain.NewsPersona, _ => CompanionBrain.OcrPersona };
            string request = scene.Kind switch { "Weather" => CompanionBrain.WeatherRequest(scene.Context), "News" => CompanionBrain.NewsRequest(scene.Context), _ => CompanionBrain.OcrSpeechRequest(scene.Context) };
            var timer = Stopwatch.StartNew(); double? first = null;
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(100));
            var text = await (Task<string>)complete.Invoke(brain, [persona, request, CompanionDialogue.Temperature,
                cancellation.Token, null, (Action<string>)(_ => first ??= timer.Elapsed.TotalMilliseconds)])!;
            records.Add(new { scene.Id, scene.Kind, scene.Context, request, run, text, firstMilliseconds = first, totalMilliseconds = timer.Elapsed.TotalMilliseconds });
            File.WriteAllText(outputFile, JsonSerializer.Serialize(new { model = "Qwen3.5-4B-heretic-Q4_K_M", actualRunnerPid = brain.RunnerPid,
                syntheticContexts = true, persona = CompanionBrain.OcrPersona, weatherPersona = CompanionBrain.WeatherPersona,
                newsPersona = CompanionBrain.NewsPersona, records }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"{scene.Id} [{run}] {text}");
        }
        brain.Stop();
        Console.WriteLine($"RESULT {records.Count} real-model generations; supplied scenarios, not a claim of real foreground capture");
        return 0;
    }
}
