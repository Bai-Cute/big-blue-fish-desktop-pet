using System;
using System.Diagnostics;
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
    internal int? RunnerPid => runner is { HasExited: false } ? runner.Id : null;

    internal CompanionBrain() => weather = new CompanionWeather(publicWeb);

    internal const string WeatherPersona = "你是蓝色大肥鱼，一位活泼亲昵的鲸鱼娘女仆，称用户为主人。主人刚主动请求天气。请把本轮给定的天气资料改写成自然中文播报：先说所选地点，再说当前天气和气温，适当提今天高低温或降水概率，并给一句有依据的简短关心。只说天气，不谈新闻或用户活动。最多三个短句、90个中文字，不加标题、列表、思考过程或动作旁白。严格依照资料，不编造实测、未来降雨时间、温度变化或预警。概率不是已经发生的事实。";
    internal static string WeatherRequest(string context) => "主人选择的地点与最新天气资料：\n" + context + "\n请直接用自然语言播报这份天气。";

    internal const string Persona = "你是蓝色大肥鱼，一位日式二次元风格的可爱鲸鱼娘女仆。称呼用户为主人，亲昵、活泼、俏皮，有一点小调皮。根据此刻状态主动冒出一句有趣的话：早晚问候、撒娇关心、轻轻吐槽摸鱼、惊叹夸奖或好奇地碎碎念。可以用哦～、呀、呢、诶嘿等语气，偶尔提小尾巴或女仆的小心思，不要每次重复。允许不求回答的自言自语式疑问，不催主人回复，不提供问答服务。每次只输出1到2个短句，共20到65个中文字，最多80字。自然口语，不加标题、引号、动作旁白、表情符号或思考过程。不要每次都说安静陪伴、文字慢慢长大，也不要总劝休息；开头和内容要有变化。只围绕本轮明确提供的一种状态说话，不扩展到未知话题。应用和标题可以用来判断主人在做什么，但没读过的文章正文、新闻细节不能编造；可以热情夸主人认真、厉害，不声称任务已完成。没有应用信息就不猜主人在写代码或看论文。没有天气观测就绝不谈天气，包括凉快、降温；只有提供温度变化才能说降温，有依据时可以贴心提醒添衣。以下只示范口吻，绝不是本轮事实：早晨问候→早上好主人，小女仆今天也元气满满地来报到啦～；前台是知乎→看起来，主人正在刷知乎摸鱼呢，不知道今天知乎上有什么好玩的呢；前台是论文→哇，好厉害的论文呀！主人认真起来的样子真棒～；前台是绘画→主人又在画漂亮的东西啦，偷偷给小女仆留个出镜位置也可以哦～。";
    internal static string SpeechRequest(string context) => "本轮已知状态：\n" + context + "\n优先围绕主人的当前应用活动，用小女仆口吻说20到65字，可以关心、打趣或夸奖。只选一个有明确依据的话题，没有提供的天气、温度变化和应用活动均未知，不从示例补充事实。不要向主人索要回答。";
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
        if (runner is { HasExited: false })
            return;
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
        // Small microbatches avoid corrupt Vulkan output on this Qwen35 / Intel driver combination.
        foreach (var arg in new[]
        {
            "-m",
            model,
            "--host",
            "127.0.0.1",
            "--port",
            port.ToString(),
            "-c",
            "2048",
            "-ngl",
            UseGpu ? "99" : "0",
            "-ub",
            UseGpu ? "32" : "512",
            "-t",
            "4",
            "--parallel",
            "1",
            "--no-webui",
            "--log-disable",
            "--jinja",
            "--reasoning",
            "off"
        }

        )
            psi.ArgumentList.Add(arg);
        runner = Process.Start(psi) ?? throw new IOException("无法启动本地模型");
        if (!job.Attach(runner))
        {
            runner.Kill(true);
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
        for (int i = 0; i < 120; i++)
        {
            token.ThrowIfCancellationRequested();
            if (!CanRunOnCurrentPower)
                throw new OperationCanceledException();
            if (runner.HasExited)
                throw new IOException("本地模型启动失败");
            try
            {
                using var r = await local.GetAsync($"http://127.0.0.1:{port}/health", token);
                if (r.IsSuccessStatusCode)
                    return;
            }
            catch (HttpRequestException)
            {
            }

            await Task.Delay(500, token);
        }

        throw new TimeoutException("模型加载超时");
    }

    private async Task<string> WeatherContext(CancellationToken token)
    {
        try { return await weather.Context(WeatherRegionCode, false, token); }
        catch (Exception e) when (e is not OperationCanceledException) { return ""; }
    }

    internal async Task<string> GenerateWeather()
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
            var text = await Complete(WeatherPersona, WeatherRequest(context), .4, token);
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

    private async Task<string> Complete(string persona, string request, double temperature, CancellationToken token)
    {
        using var response = await local.PostAsJsonAsync($"http://127.0.0.1:{port}/v1/chat/completions",
            new { model = "local", messages = new[] { new { role = "system", content = persona }, new { role = "user", content = request } },
                temperature, top_p = .9, max_tokens = persona == WeatherPersona ? 220 : 180, stream = false,
                chat_template_kwargs = new { enable_thinking = false } }, token);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        return Clean(document.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "");
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

    internal async Task<string> Generate(bool usePublic, bool useForeground)
    {
        if (Busy || !CanRunOnCurrentPower)
            return "";
        Busy = true;
        active = new CancellationTokenSource(TimeSpan.FromSeconds(100));
        var token = active.Token;
        try
        {
            string? news = null;
            if (usePublic && newsGate.TryReserve(DateTime.Now, Random.Shared.NextDouble()))
                news = await NewsTitle(token);
            string request;
            if (news != null)
            {
                lastNewsTitle = news;
                request = NewsRequest(news);
            }
            else
            {
                var context = $"现在是{DateTime.Now:yyyy年M月d日 HH:mm}。" + (useForeground ? CompanionNative.ForegroundContext() : "用户正在使用电脑。");
                if (usePublic && Random.Shared.NextDouble() < .15)
                    context += "\n" + await WeatherContext(token);
                request = SpeechRequest(context);
            }

            await EnsureRunner(token);
            Status = "本地生成短句";
            var text = await Complete(news == null ? Persona : NewsPersona, request, news == null ? .65 : .4, token);
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

    private void StopRunner()
    {
        try
        {
            if (runner is { HasExited: false })
                runner.Kill(true);
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
