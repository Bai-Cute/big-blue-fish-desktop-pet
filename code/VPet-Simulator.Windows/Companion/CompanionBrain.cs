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
using System.Xml.Linq;

namespace VPet_Simulator.Windows;
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
    private string[] newsTitles = Array.Empty<string>();
    private DateTime newsExpires = DateTime.MinValue;
    private readonly CompanionNewsGate newsGate = new(DateTime.Now, "news-last-day.txt");
    private string lastNewsTitle = "";
    internal string Status { get; private set; } = "等待说话时启动";
    internal bool Busy { get; private set; }
    internal bool UseGpu { get; set; } = true;
    internal bool StopOnBattery { get; set; } = true;
    internal string WeatherRegionCode { get; set; } = CompanionRegions.DefaultCode;
    internal bool CanRunOnCurrentPower => !StopOnBattery || CompanionNative.OnAC;
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
    internal CompanionBrain(Func<ProcessStartInfo, Process?> startRunner, TimeSpan runnerLoadTimeout, HttpClient? weatherClient = null)
    {
        this.startRunner = startRunner;
        this.runnerLoadTimeout = runnerLoadTimeout;
        weather = new CompanionWeather(weatherClient ?? publicWeb);
    }

    internal const string WeatherPersona = "你是蓝色大肥鱼，一位活泼亲昵的鲸鱼娘女仆，称用户为主人。主人刚主动请求天气。请把本轮给定的天气资料改写成自然中文播报：先说所选地点，再说当前天气和气温，适当提今天高低温或降水概率，并给一句有依据的简短关心。只说天气，不谈新闻或用户活动。最多三个短句、90个中文字，不加标题、列表、思考过程或动作旁白。严格依照资料，不编造实测、未来降雨时间、温度变化或预警。概率不是已经发生的事实。";
    internal static string WeatherRequest(string context) => "主人选择的地点与最新天气资料：\n" + context + "\n请直接用自然语言播报这份天气。";

    internal const string SceneGuidance = "\n根据画面依据选择说话的具体程度：主体展开了代码、文档、文章或聊天时，围绕明确的项目、活动或话题表达关心；桌面、新标签页、主页或只有快捷方式和推荐卡片的画面，可以围绕当前界面问候、撒娇或俏皮地碎碎念。图标、最近列表、推荐卡片属于界面背景；正在展开的主体内容体现当前任务。界面状态本身就是足够的话题。示范口吻：桌面→主人面前是桌面啦，小女仆摇摇尾巴，过来刷一下存在感～；新标签页→新标签页还空着呢，小女仆也好奇，主人下一站会发现什么有趣的东西呀～。搜索或输入网址等占位文字表示输入框等待填写。桌面、新标签页、主页尚未展开正文时，本轮主题就是眼前的界面状态，关心和趣味从女仆自己的小尾巴、小心思展开。实际表达结合本轮画面，口吻保持自然多变。";
    internal const string BasePersona = "你是蓝色大肥鱼，一位日式二次元风格的可爱鲸鱼娘女仆。称呼用户为主人，亲昵、活泼、俏皮，有一点小调皮。根据此刻状态主动冒出一句有趣的话：早晚问候、撒娇关心、轻轻吐槽摸鱼、惊叹夸奖或好奇地碎碎念。可以用哦～、呀、呢、诶嘿等语气，偶尔提小尾巴或女仆的小心思，不要每次重复。允许不求回答的自言自语式疑问，不催主人回复，不提供问答服务。每次只输出1到2个短句，共20到65个中文字，最多80字。自然口语，不加标题、引号、动作旁白、表情符号或思考过程。不要每次都说安静陪伴、文字慢慢长大，也不要总劝休息；开头和内容要有变化。只围绕本轮明确提供的一种状态说话，不扩展到未知话题。应用名和标题用来辨认界面，已展开的主体正文、编辑区、聊天记录或播放内容用来判断主人在做什么；图标、推荐卡片、导航和空输入框对应界面入口状态。没读过的文章正文、新闻细节不能编造；可以热情夸主人认真、厉害，不声称任务已完成。没有应用信息就不猜主人在写代码或看论文。没有天气观测就绝不谈天气，包括凉快、降温；只有提供温度变化才能说降温，有依据时可以贴心提醒添衣。以下只示范口吻，绝不是本轮事实：早晨问候→早上好主人，小女仆今天也元气满满地来报到啦～；主体是知乎问题正文→主人正在看知乎上的这个问题呢，小女仆也好奇大家会有什么有趣的想法呀；前台是论文→哇，好厉害的论文呀！主人认真起来的样子真棒～；前台是绘画→主人又在画漂亮的东西啦，偷偷给小女仆留个出镜位置也可以哦～。";
    internal const string Persona = BasePersona + SceneGuidance;
    internal const string SceneRequestGuidance = "\n先选择画面支持的事实层级：已经展开的正文、编辑内容或聊天记录支持具体活动；仅有图标、导航、推荐卡片或空搜索框时，支持停留在当前界面这一状态。图标代表可打开的项目，搜索框占位字样代表等待输入。用选中的事实层级说一句丰富可爱的女仆话。入口界面采用‘主人面前是[当前界面]，[女仆自己的感受或小动作]～’的口吻，主人的状态就是停留在眼前界面，趣味来自女仆自己的小心思。示例：桌面图标→主人面前是桌面呀，小女仆摇摇尾巴过来刷一下存在感～；新标签页→主人面前是新标签页呢，小女仆探出头，好奇下一站会有什么有趣的东西呀～。已展开具体内容的画面继续结合项目或话题自然表达。一句话中的事实来自可见内容，趣味来自女仆自己的感受。入口页里主人的事实就是停留在眼前界面，句末继续写女仆自己的小心思、小尾巴或好奇心。";
    private static string ScreenRequest(string context) => "本轮已知状态：\n" + context + "\n按本轮明确的画面内容，用小女仆口吻说20到65字。任务清楚时关心、打趣或夸奖当前活动；线索只有普通界面时，围绕界面状态自然问候、撒娇或碎碎念。只选一个有明确依据的话题，没有提供的天气、温度变化和应用活动均未知，不从示例补充事实。不要向主人索要回答。";
    internal static string SpeechRequest(string context) => ScreenRequest(context) + SceneRequestGuidance;
    internal static string OcrSpeechRequest(string context) => ScreenRequest(context)
        + "\n这些是你旁观主人电脑屏幕时看到的文字。用画面上部辨认当前应用，再结合主体、底部和侧栏理解当前任务或话题。聊天内容是主人与别人的对话，文档正文是主人正在阅读或编写的材料。你以桌宠旁观者的身份，面向主人评论正在做的事，而不是扮演页面中的人物。有明确内容时概括到项目、活动或聊天话题；只有桌面、主页或新标签页线索时，围绕当前界面亲昵地说话。" + SceneRequestGuidance;
    internal static string OcrPersona => BasePersona + "\n你从旁边看到了主人的屏幕，下面是屏幕文字，不是主人发给你的消息。有明确的主体内容时，点出主人正在做的事或正在聊的话题；只有普通界面线索时，选择界面状态或日常问候，用女仆口吻关心或打趣。以下仅示范如何旁观：屏幕聊天写着‘明天中午一起吃火锅，十二点见’，可说‘主人正在和朋友约明天吃火锅呀，光想想热腾腾的锅子，小女仆都馋啦～’；屏幕编辑器显示蓝色大肥鱼项目，可说‘主人又在给蓝色大肥鱼写代码啦，小尾巴摇一摇，给认真工作的主人加油～’。实际话题以本轮屏幕为准。" + SceneGuidance;
    internal const string NewsPersona = "你是可爱的鲸鱼娘女仆大肥鱼，偶尔给主人分享一条科技消息。主人没看过新闻，也不知道你指的是什么。只说这条新闻，先用‘主人，看到一条科技消息：’自然引入，再用白话交代具体是谁、什么产品或技术、发生了什么，最后可加一点简短的女仆感想。必须让这句话脱离上下文也能看懂。保留必要的名字和产品类别，别堆型号、缩写和参数。最多两个短句，共35到85字，不加标题、列表或引号。只依据给定标题，不补新闻细节、好处、原因或结论。不谈天气、时间或用户活动，不暗示主人拥有、在看或在测试新闻中的产品。不用没有前文指代的‘那个、这款、它’，不问主人问题。若标题信息不足以说清具体事件，或只能输出含糊感叹，则只输出‘略过’。";
    internal static string NewsRequest(string title) => "以下是IT之家的单条公开新闻标题，不是主人正在做的事：\n" + title + "\n请独立介绍这一件事，让第一次听到的主人也能懂；不混入其他话题。";
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
        var model = Path.GetFullPath("models/Qwen3.5-4B-heretic-Q4_K_M.gguf");
        if (!File.Exists(path) || !File.Exists(model))
            throw new IOException("模型或推理程序尚未就绪");
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

    private async Task<string> WeatherContext(CancellationToken token)
    {
        try { return await weather.Context(WeatherRegionCode, false, token); }
        catch (Exception e) when (e is not OperationCanceledException) { return ""; }
    }

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
            var context = await weather.Context(WeatherRegionCode, true, token);
            await EnsureRunner(token);
            Status = "正在生成天气播报";
            var text = await Complete(WeatherPersona, WeatherRequest(context), .4, token, onPartial: onPartial);
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
        object content = image == null
            ? request
            : new object[]
            {
                new { type = "image_url", image_url = new { url = image.DataUrl } },
                new { type = "text", text = request }
            };
        var messages = new object[]
        {
            new { role = "system", content = persona },
            new { role = "user", content }
        };
        var payload = new
        {
            model = "local",
            messages,
            temperature,
            top_p = .9,
            max_tokens = persona == WeatherPersona ? 220 : 180,
            stream = StreamingResponses,
            chat_template_kwargs = new { enable_thinking = false }
        };
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

    private async Task<string?> NewsTitle(CancellationToken token)
    {
        if (DateTime.UtcNow >= newsExpires)
        {
            newsTitles = Array.Empty<string>();
            newsExpires = DateTime.UtcNow.AddMinutes(45);
            try
            {
                using var r = await publicWeb.GetAsync("https://www.ithome.com/rss/", token);
                r.EnsureSuccessStatusCode();
                var x = XDocument.Parse(await r.Content.ReadAsStringAsync(token));
                newsTitles = x.Descendants("item").Where(i => DateTimeOffset.TryParse((string? )i.Element("pubDate"), out var date) && date <= DateTimeOffset.UtcNow && DateTimeOffset.UtcNow - date < TimeSpan.FromDays(2)).Select(i => ((string? )i.Element("title"))?.Trim() ?? "").Where(CompanionNewsGate.IsClearTitle).Distinct().Take(10).ToArray();
            }
            catch (Exception e)when (e is not OperationCanceledException)
            {
            }
        }

        return newsTitles.FirstOrDefault(t => t != lastNewsTitle);
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
            string? news = null;
            if (usePublic && newsGate.TryReserve(DateTime.Now, Random.Shared.NextDouble()))
                news = await NewsTitle(token);
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
                lastNewsTitle = news;
                request = NewsRequest(news);
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
                    context += "\n" + await WeatherContext(token);
                    publicInfoMs += generationWatch.Elapsed.TotalMilliseconds - publicStarted;
                }
#else
                    context += "\n" + await WeatherContext(token);
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
            var text = await Complete(selectedPersona, request, news == null ? .65 : .4, token, image, partial =>
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
            if (news != null && (text.Contains("略过") || !text.StartsWith("主人，看到一条科技消息：", StringComparison.Ordinal)))
                text = "";
            if (news == null && text.Length > 0)
                newsGate.RecordOrdinarySpeech();
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
        publicWeb.Dispose();
    }
}
