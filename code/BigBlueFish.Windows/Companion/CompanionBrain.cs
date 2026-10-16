using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace BigBlueFish.Windows;
internal sealed class CompanionBrain : IDisposable
{
    private readonly HttpClient local = new(new HttpClientHandler { UseProxy = false })
    {
        Timeout = TimeSpan.FromSeconds(100)
    };
    private readonly HttpClient publicWeb = new()
    {
        Timeout = TimeSpan.FromSeconds(12)
    };
    private Process? runner;
    private bool runnerReady;
    private readonly Func<ProcessStartInfo, Process?> startRunner;
    private readonly TimeSpan runnerLoadTimeout;
    private readonly CompanionJob job = new();
    private int port;
    private CancellationTokenSource? active;
    private readonly CompanionWeather weather;
    private CompanionNews.Item[] newsItems = Array.Empty<CompanionNews.Item>();
    private DateTime newsExpires = DateTime.MinValue;
    private readonly Func<double> newsRoll;
    private string lastNewsTitle = "";
    internal string Status { get; private set; } = "等待说话时启动";
    internal bool Busy { get; private set; }
    internal bool UseGpu { get; set; } = true;
    internal bool StopOnBattery { get; set; } = true;
    internal string WeatherRegionCode { get; set; } = CompanionRegions.DefaultCode;
    internal bool CanRunOnCurrentPower => !StopOnBattery || CompanionNative.OnAC;
    private DateTime nextModelCheck;
    private bool modelAvailable;
    internal bool ModelAvailable
    {
        get
        {
            if (DateTime.UtcNow >= nextModelCheck)
            {
                modelAvailable = BigBlueFish.Deployment.ModelStore.Resolve(Environment.CurrentDirectory) != null;
                nextModelCheck = DateTime.UtcNow.AddSeconds(2);
            }
            return modelAvailable;
        }
    }
    internal void RefreshModelAvailability() => nextModelCheck = DateTime.MinValue;
#if COMPANION_OCR
    internal bool VisionAvailable => false;
    internal const string InputMode = "OCR";
#else
    internal bool VisionAvailable => File.Exists(Path.GetFullPath("models/Qwen3.5-4B-heretic.mmproj-f16.gguf"));
    internal const string InputMode = "Vision";
#endif
    internal int? RunnerPid => runner is { HasExited: false } ? runner.Id : null;
    // Temporary experiment: show streamed model output as it arrives.
    internal static bool StreamingResponses { get; set; } = true;

    internal CompanionBrain() : this(Process.Start, TimeSpan.FromSeconds(60)) { }

    // The verification harness substitutes a real child process, not model output in the app.
    internal CompanionBrain(Func<ProcessStartInfo, Process?> startRunner, TimeSpan runnerLoadTimeout, HttpClient? weatherClient = null, string? weatherCachePath = null, Func<double>? newsRoll = null)
    {
        this.startRunner = startRunner;
        this.runnerLoadTimeout = runnerLoadTimeout;
        this.newsRoll = newsRoll ?? Random.Shared.NextDouble;
        weather = new CompanionWeather(weatherClient ?? publicWeb, weatherCachePath ?? (weatherClient == null ? "cache/weather.json" : null));
    }

    internal const string WeatherPersona = CompanionDialogue.WeatherPersona;
    internal static string WeatherRequest(string context) => "本轮天气资料：\n" + context;
    internal const string BasePersona = CompanionDialogue.Voice;
    internal const string SceneGuidance = CompanionDialogue.SceneGuidance;
    internal const string Persona = BasePersona + SceneGuidance + CompanionDialogue.ScreenReadingGuidance;
    private static string ScreenRequest(string context) => "【本轮屏幕资料】\n" + context + "\n【屏幕资料结束】";
    internal static string SpeechRequest(string context) => ScreenRequest(context);
    internal static string OcrSpeechRequest(string context) => ScreenRequest(context);
    internal static string OcrPersona => Persona + CompanionDialogue.OcrObserverGuidance;
    internal const string NewsPersona = CompanionDialogue.NewsPersona;
    internal static string NewsRequest(string context) => $"当前时间：{DateTime.Now:yyyy-MM-dd HH:mm}\n【本轮单条科技新闻资料】\n" + context
        + "\n【新闻资料结束】\n请直接对主人说两小句：先说这条新闻最重要的事，再说一点自己的感受或对主人的关切。合起来约30–60字。";

    internal sealed record ChatMessage(string role, object content);
    internal static ChatMessage[] BuildMessages(string persona, string request, CompanionWindowImage? image = null)
    {
        if (persona == Persona || persona == OcrPersona)
            persona += "\n" + CompanionDialogue.ReactionHint()
                + "\n本次只说鱼鱼最想说的那一小句，一个自然可爱的小反应就好。";
        object content = image == null ? request : new object[]
        {
            new { type = "image_url", image_url = new { url = image.DataUrl } },
            new { type = "text", text = request }
        };
        return [new("system", persona), new("user", content)];
    }
    internal static string Clean(string text)
    {
        text = Regex.Replace(text, @"<think>[\s\S]*?</think>", "", RegexOptions.IgnoreCase);
        if (text.Contains("<think>", StringComparison.OrdinalIgnoreCase))
            return "";
        text = Regex.Replace(text, @"\s+", " ").Trim().Trim('"', '“', '”');
        var runes = text.EnumerateRunes().ToArray();
        if (!Regex.IsMatch(text, @"[\p{L}\p{N}]") || Regex.IsMatch(text, @"(.)\1{8}"))
            return "";
        if (runes.Length < 100)
            return text;
        var shortText = string.Concat(runes.Take(98).Select(x => x.ToString()));
        var end = shortText.LastIndexOfAny(new[] { '。', '！', '？', '～' });
        return end >= 15 ? shortText[..(end + 1)] : shortText + "…";
    }

    private async Task EnsureRunner(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (runnerReady && runner is { HasExited: false })
        {
            try
            {
                using var healthTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                healthTimeout.CancelAfter(TimeSpan.FromSeconds(3));
                using var health = await local.GetAsync($"http://127.0.0.1:{port}/health", healthTimeout.Token);
                if (health.IsSuccessStatusCode) return;
            }
            catch (HttpRequestException) { }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
        }
        StopRunner();
        token.ThrowIfCancellationRequested();
        try
        {
            await StartRunner(token);
        }
        catch
        {
            StopRunner();
            throw;
        }
    }

    private async Task StartRunner(CancellationToken token)
    {
        var path = Path.GetFullPath("runtime/llama-server.exe");
        var model = BigBlueFish.Deployment.ModelStore.Resolve(Environment.CurrentDirectory);
        if (!File.Exists(path)) throw new IOException("未找到本地推理程序：" + path);
        if (model == null) throw new IOException("尚未安装可用模型，可以在设置中安装模型。");
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        var psi = new ProcessStartInfo(path)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        // Text and multimodal prefill have each been tested with this Intel Vulkan/Qwen build.
        // Intermediate ubatches and auto Flash Attention can corrupt output; GPU ubatch 2048
        // with FA off keeps coherent output and improves first-token latency in both routes.
        var arguments = new List<string>
        {
            "-m",
            model,
            "--host",
            "127.0.0.1",
            "--port",
            port.ToString(),
            "-c",
#if COMPANION_OCR
            "8192",
#else
            VisionAvailable ? "8192" : "2048",
#endif
            "-ngl",
            UseGpu ? "99" : "0",
            "-ub",
            UseGpu ? "2048" : "512",
            "-b",
            "2048",
            "-fa",
            "off",
            "-t",
            "4",
            "--parallel",
            "1",
            "--no-webui",
            "--log-disable",
            "--jinja",
            "--reasoning",
            "off"
        };
        if (VisionAvailable)
        {
            arguments.Add("--mmproj");
            arguments.Add(Path.GetFullPath("models/Qwen3.5-4B-heretic.mmproj-f16.gguf"));
            arguments.Add("--image-max-tokens");
            arguments.Add("512");
        }
        foreach (var arg in arguments)
            psi.ArgumentList.Add(arg);
        runner = startRunner(psi) ?? throw new IOException("无法启动本地模型");
        if (!job.Attach(runner))
        {
            throw new IOException("无法建立模型退出保障");
        }

        runner.OutputDataReceived += (_, _) =>
        {
        };
        runner.ErrorDataReceived += (_, _) =>
        {
        };
        runner.BeginOutputReadLine();
        runner.BeginErrorReadLine();
        Status = "正在加载本地模型";
        using var loading = CancellationTokenSource.CreateLinkedTokenSource(token);
        loading.CancelAfter(runnerLoadTimeout);
        var loadingToken = loading.Token;
        try
        {
            while (true)
            {
                loadingToken.ThrowIfCancellationRequested();
                if (!CanRunOnCurrentPower)
                    throw new OperationCanceledException();
                if (runner.HasExited)
                    throw new IOException("本地模型启动失败");
                try
                {
                    using var r = await local.GetAsync($"http://127.0.0.1:{port}/health", loadingToken);
                    if (r.IsSuccessStatusCode)
                    {
                        runnerReady = true;
                        return;
                    }
                }
                catch (HttpRequestException) { }
                await Task.Delay(500, loadingToken);
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested && CanRunOnCurrentPower)
        {
            throw new TimeoutException("模型加载超时");
        }
    }

    internal void RefreshWeather() => _ = weather.RefreshIfDue(WeatherRegionCode);

    private string WeatherContext() => weather.CachedContext(WeatherRegionCode);

    internal async Task<string> GenerateWeather(Action<string>? onPartial = null)
    {
        if (Busy || !CanRunOnCurrentPower)
            return "";
        Busy = true;
        active = new CancellationTokenSource(TimeSpan.FromSeconds(100));
        var token = active.Token;
        try
        {
            Status = "正在获取所选地区天气";
            var context = await weather.Context(WeatherRegionCode, token);
            await EnsureRunner(token);
            Status = "正在生成天气播报";
            var text = await Complete(WeatherPersona, WeatherRequest(context), CompanionDialogue.Temperature, token, onPartial: onPartial);
            Status = "本地模型就绪";
            return CanRunOnCurrentPower ? text : "";
        }
        catch (OperationCanceledException) { Status = "天气播报已取消"; return ""; }
        catch (Exception e)
        {
            Status = e is IOException ? e.Message : "暂时无法获取或播报天气，请稍后重试";
            return "";
        }
        finally
        {
            Busy = false;
            active?.Dispose();
            active = null;
            if (!CanRunOnCurrentPower) StopRunner();
        }
    }

    private async Task<string> Complete(string persona, string request, double temperature, CancellationToken token, CompanionWindowImage? image = null, Action<string>? onPartial = null)
    {
        try
        {
            return await CompleteRequest(persona, request, temperature, token, image, onPartial);
        }
        catch (Exception error) when (error is HttpRequestException or IOException)
        {
            StopRunner();
            throw;
        }
    }

    private async Task<string> CompleteRequest(string persona, string request, double temperature, CancellationToken token, CompanionWindowImage? image = null, Action<string>? onPartial = null)
    {
        var messages = BuildMessages(persona, request, image);
        var payload = new
        {
            model = "local",
            messages,
            temperature,
            top_p = .9,
            max_tokens = persona == NewsPersona ? 160 : persona == WeatherPersona ? 220 : 180,
            stream = StreamingResponses,
            chat_template_kwargs = new { enable_thinking = false }
        };
        if (App.Args.Contains("--test-mode"))
        {
            try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "model-request.json"), JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true })); }
            catch { }
        }
        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{port}/v1/chat/completions")
        {
            Content = JsonContent.Create(payload)
        };
        using var response = await local.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        if (!StreamingResponses)
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            return Clean(document.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "");
        }

        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(token));
        var raw = new StringBuilder();
        var lastPartial = "";
        while (await reader.ReadLineAsync(token) is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                continue;
            var data = line[5..].Trim();
            if (data == "[DONE]")
                break;
            try
            {
                using var chunk = JsonDocument.Parse(data);
                if (!chunk.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                    continue;
                var choice = choices[0];
                string piece = "";
                if (choice.TryGetProperty("delta", out var delta) && delta.TryGetProperty("content", out var deltaContent))
                    piece = deltaContent.GetString() ?? "";
                else if (choice.TryGetProperty("message", out var message) && message.TryGetProperty("content", out var messageContent))
                    piece = messageContent.GetString() ?? "";
                if (piece.Length == 0)
                    continue;
                raw.Append(piece);
                var partial = Clean(raw.ToString());
                if (partial.Length > 0 && partial != lastPartial)
                {
                    lastPartial = partial;
                    onPartial?.Invoke(partial);
                }
            }
            catch (JsonException)
            {
            }
        }

        return Clean(raw.ToString());
    }

    private async Task<CompanionNews.Item?> NewsItem(CancellationToken token)
    {
        if (DateTime.UtcNow >= newsExpires)
        {
            try
            {
                using var r = await publicWeb.GetAsync("https://www.ithome.com/rss/", token);
                r.EnsureSuccessStatusCode();
                newsItems = CompanionNews.ReadFeed(await r.Content.ReadAsStringAsync(token), DateTimeOffset.UtcNow);
                newsExpires = DateTime.UtcNow.AddMinutes(45);
            }
            catch (Exception e)when (e is not OperationCanceledException)
            {
                newsItems = newsItems.Where(i => DateTimeOffset.UtcNow - i.Published < TimeSpan.FromDays(2)).ToArray();
                newsExpires = DateTime.UtcNow.AddMinutes(2);
            }
        }

        var candidates = newsItems.Where(i => i.Title != lastNewsTitle
            && DateTimeOffset.UtcNow - i.Published < TimeSpan.FromDays(2)).ToArray();
        return candidates.Length == 0 ? null : candidates[Random.Shared.Next(candidates.Length)];
    }

    internal async Task<string> Generate(bool usePublic, bool useForeground, IntPtr foregroundHandle = default, Action<string>? onPartial = null)
    {
        if (Busy || !CanRunOnCurrentPower)
            return "";
        Busy = true;
        active = new CancellationTokenSource(TimeSpan.FromSeconds(100));
        var token = active.Token;
#if COMPANION_OCR
        var generationWatch = Stopwatch.StartNew();
        double? firstContentMs = null;
        double publicInfoMs = 0, inputReadyMs = 0, runnerReadyMs = 0;
#else
        var generationWatch = Stopwatch.StartNew();
        double? firstContentMs = null, runnerReadyMs = null;
#endif
        try
        {
            CompanionNews.Item? news = null;
            if (usePublic && CompanionNews.ShouldSelect(newsRoll()))
                news = await NewsItem(token);
#if COMPANION_OCR
            publicInfoMs = generationWatch.Elapsed.TotalMilliseconds;
#endif
            string request;
            CompanionWindowImage? image = null;
#if COMPANION_OCR
            CompanionOcrSnapshot? ocr = null;
#endif
            if (news != null)
            {
                request = NewsRequest(news.Context);
            }
            else
            {
#if COMPANION_OCR
                string context = $"现在是{DateTime.Now:yyyy年M月d日 HH:mm}。\n";
                if (useForeground)
                {
                    // Pin exactly one HWND for both the capture and the supplemental identity.
                    var handle = foregroundHandle == IntPtr.Zero ? CompanionNative.GetForegroundWindow() : foregroundHandle;
                    Status = "正在识别当前窗口文字";
                    ocr = await CompanionOcr.ReadAsync(handle, token);
                    context += ocr.Context;
                }
                else context += "用户正在使用电脑。";
#else
                var context = $"现在是{DateTime.Now:yyyy年M月d日 HH:mm}。" + (useForeground ? CompanionNative.ForegroundContext(foregroundHandle) : "用户正在使用电脑。");
                if (useForeground && VisionAvailable)
                    image = CompanionNative.ForegroundWindowImage(foregroundHandle);
#endif
                if (usePublic && Random.Shared.NextDouble() < .15)
#if COMPANION_OCR
                {
                    double publicStarted = generationWatch.Elapsed.TotalMilliseconds;
                    context += "\n" + WeatherContext();
                    publicInfoMs += generationWatch.Elapsed.TotalMilliseconds - publicStarted;
                }
#else
                    context += "\n" + WeatherContext();
#endif
#if COMPANION_OCR
                request = ocr == null ? SpeechRequest(context) : OcrSpeechRequest(context);
                inputReadyMs = generationWatch.Elapsed.TotalMilliseconds;
                WriteOcrDiagnostic(ocr, request, null, false, inputReadyMs, null, publicInfoMs, inputReadyMs, null);
#else
                request = SpeechRequest(context);
                WriteVisionDiagnostic(image, context, null, false);
#endif
            }

            await EnsureRunner(token);
            runnerReadyMs = generationWatch.Elapsed.TotalMilliseconds;
            Status = image == null ? "本地生成短句" : "正在理解当前窗口";
#if COMPANION_OCR
            var selectedPersona = news == null ? (ocr == null ? Persona : OcrPersona) : NewsPersona;
#else
            var selectedPersona = news == null ? Persona : NewsPersona;
#endif
            var text = await Complete(selectedPersona, request, news == null ? CompanionDialogue.Temperature : .35, token, image, partial =>
            {
                firstContentMs ??= generationWatch.Elapsed.TotalMilliseconds;
                onPartial?.Invoke(partial);
            });
#if COMPANION_OCR
            WriteOcrDiagnostic(ocr, request, text, true, generationWatch.Elapsed.TotalMilliseconds, firstContentMs,
                publicInfoMs, inputReadyMs, runnerReadyMs);
#else
            WriteVisionDiagnostic(image, request, text, true, generationWatch.Elapsed.TotalMilliseconds,
                firstContentMs, runnerReadyMs);
#endif
            if (news != null && text.Length > 0)
                lastNewsTitle = news.Title;
            Status = "本地模型就绪";
            return CanRunOnCurrentPower ? text : "";
        }
        catch (OperationCanceledException)
        {
            Status = "模型已暂停";
            return "";
        }
        catch (Exception e)
        {
            Status = e is IOException ? e.Message : "暂时无法生成，稍后重试";
            return "";
        }
        finally
        {
            Busy = false;
            active?.Dispose();
            active = null;
            if (!CanRunOnCurrentPower)
                StopRunner();
        }
    }

#if COMPANION_OCR
    private static void WriteOcrDiagnostic(CompanionOcrSnapshot? ocr, string request, string? response,
        bool completed, double elapsedMs, double? firstContentMs, double publicInfoMs, double inputReadyMs,
        double? runnerReadyMs)
    {
        if (!App.Args.Contains("--test-mode") || ocr == null) return;
        try
        {
            var status = new
            {
                timestamp = DateTimeOffset.Now,
                version = typeof(CompanionBrain).Assembly.GetName().Version?.ToString(), inputMode = InputMode,
                imageAttached = false, imageWidth = ocr.Width, imageHeight = ocr.Height,
                language = ocr.Language, wordCount = ocr.WordCount,
                windowHandle = ocr.WindowHandle, isDesktop = ocr.IsDesktop,
                captureMs = ocr.CaptureMilliseconds, ocrMs = ocr.RecognizeMilliseconds,
                elapsedMs, firstContentMs, publicInfoMs, inputReadyMs, runnerReadyMs,
                modelToFirstContentMs = firstContentMs - runnerReadyMs,
                requestCompleted = completed, request, response, words = ocr.Words
            };
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "ocr-status.json"),
                JsonSerializer.Serialize(status, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
#endif

    private static void WriteVisionDiagnostic(CompanionWindowImage? image, string request, string? response, bool requestCompleted,
        double? elapsedMs = null, double? firstContentMs = null, double? runnerReadyMs = null)
    {
        if (!App.Args.Contains("--test-mode") || image == null)
            return;
        try
        {
            var baseDirectory = AppContext.BaseDirectory;
            image.SaveJpeg(Path.Combine(baseDirectory, "vision-input.jpg"));
            var status = new
            {
                timestamp = DateTimeOffset.Now,
                version = typeof(CompanionBrain).Assembly.GetName().Version?.ToString(), inputMode = InputMode,
                imageAttached = true,
                imageWidth = image.Width,
                imageHeight = image.Height,
                elapsedMs, firstContentMs, runnerReadyMs,
                modelToFirstContentMs = firstContentMs - runnerReadyMs,
                requestCompleted,
                request,
                response
            };
            File.WriteAllText(Path.Combine(baseDirectory, "vision-status.json"), JsonSerializer.Serialize(status, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
        }
    }

    private void StopRunner()
    {
        runnerReady = false;
        try
        {
            if (runner is { HasExited: false })
            {
                runner.Kill(true);
                runner.WaitForExit(5000);
            }
        }
        catch
        {
        }

        runner?.Dispose();
        runner = null;
    }

    internal void Stop()
    {
        active?.Cancel();
        StopRunner();
        Status = "模型已停止";
    }

    internal async Task WarmUp()
    {
        if (Busy || RunnerPid != null || !CanRunOnCurrentPower)
            return;
        Busy = true;
        active = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        try
        {
            await EnsureRunner(active.Token);
            Status = "本地模型已驻留";
        }
        catch (Exception)
        {
            StopRunner();
            Status = "模型未就绪，稍后重试";
        }
        finally
        {
            Busy = false;
            active?.Dispose();
            active = null;
        }
    }

    internal void CancelGeneration()
    {
        active?.Cancel();
    }

    public void Dispose()
    {
        Stop();
        job.Dispose();
        local.Dispose();
        weather.Dispose();
        publicWeb.Dispose();
    }
}
