using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Linq;
using VPet_Simulator.Windows;

class Program
{
    static async Task Main(string[] args)
    {
        if (args.Length > 1 && args[0] == "capture-desktop")
        {
            bool useVision = args.Contains("--vision");
            var handle = CompanionNative.GetShellWindow();
            var screenshot = CompanionScreenCapture.Capture(handle, useVision ? 768 : 2560, !useVision)
                ?? throw new Exception("Desktop capture failed");
            string context = useVision ? CompanionNative.ForegroundContext(handle)
                : (await CompanionOcr.RecognizeAsync(screenshot, CompanionNative.ForegroundIdentity(handle), 0, CancellationToken.None)).Context;
            await File.WriteAllTextAsync(args[1], JsonSerializer.Serialize(new
            {
                persona = useVision ? CompanionBrain.Persona : CompanionBrain.OcrPersona,
                request = useVision ? CompanionBrain.SpeechRequest(context) : CompanionBrain.OcrSpeechRequest(context),
                image = useVision ? screenshot.DataUrl : null, width = screenshot.Width, height = screenshot.Height
            }));
            screenshot.SaveJpeg(Path.ChangeExtension(args[1], useVision ? ".jpg" : ".png"));
            Console.WriteLine($"Captured real desktop {handle}: {screenshot.Width}x{screenshot.Height}, imageAttached={useVision}");
            return;
        }
        if (args.Length > 1 && args[0] == "capture-vision")
        {
            var handle = CompanionNative.GetForegroundWindow();
            var screenshot = CompanionScreenCapture.Capture(handle)
                ?? throw new Exception("No foreground screenshot available");
            var context = $"现在是{DateTime.Now:yyyy年M月d日 HH:mm}。" + CompanionNative.ForegroundContext(handle);
            await File.WriteAllTextAsync(args[1], JsonSerializer.Serialize(new
            {
                persona = CompanionBrain.Persona, request = CompanionBrain.SpeechRequest(context),
                image = screenshot.DataUrl, width = screenshot.Width, height = screenshot.Height
            }));
            screenshot.SaveJpeg(Path.ChangeExtension(args[1], ".jpg"));
            Console.WriteLine($"Captured foreground {handle}: {screenshot.Width}x{screenshot.Height}, context chars={context.Length}");
            return;
        }
        var fixtures = new[]
        {
            ("development", "Visual Studio Code", new[] { "蓝色大肥鱼项目", "CompanionOcr.cs", "正在开发前台截图的文字识别功能", "编译成功" }, "蓝色大肥鱼"),
            ("chat", "微信", new[] { "小白", "我：明天一起去吃饭吗？", "小白：好呀，明天中午去吃火锅吧。", "我：那就十二点见。" }, "明天"),
            ("office", "Microsoft Word", new[] { "课程报告.docx", "教育实践课程报告", "正在撰写本学期课程总结", "一、教学实践的收获" }, "课程"),
            ("browser", "科技新闻 - 浏览器", new[] { "科技新闻", "新一代笔记本电脑发布", "阅读最新科技新闻与电脑评测" }, "科技"),
            ("video", "哔哩哔哩", new[] { "视频教程", "Photoshop 绘画入门", "第十课：颜色与光影", "播放 03:21 / 20:00" }, "绘画"),
            ("desktop", "Windows 桌面", new[] { "快捷方式和文件图标", "Visual Studio Code    微信    回收站", "课程报告.docx    蓝色大肥鱼", "开始    搜索    18:30" }, "桌面"),
            ("newtab", "Microsoft Edge - 新建标签页", new[] { "搜索或输入网址", "常用网站：GitHub  知乎  ChatGPT", "推荐卡片：新一代笔记本电脑发布", "新建标签页" }, "标签"),
            ("homepage", "浏览器主页", new[] { "搜索网页", "常用网站：GitHub  微信", "推荐：科技新闻  今日天气", "主页" }, "主页")
        };
        if (args.Length > 1 && args[0] == "generate-vision")
        {
            var inputs = fixtures.Select(fixture =>
            {
                using var bitmap = new Bitmap(768, 512);
                using (var g = Graphics.FromImage(bitmap))
                using (var titleFont = new Font("Microsoft YaHei UI", 26))
                using (var bodyFont = new Font("Microsoft YaHei UI", 23))
                {
                    g.Clear(Color.White);
                    g.DrawString(fixture.Item2, titleFont, Brushes.Black, 20, 12);
                    for (int i = 0; i < fixture.Item3.Length; i++)
                        g.DrawString(fixture.Item3[i], bodyFont, Brushes.Black, 30, 110 + i * 85);
                }
                using var memory = new MemoryStream();
                bitmap.Save(memory, ImageFormat.Jpeg);
                return new { scene = fixture.Item1, persona = CompanionBrain.Persona,
                    request = CompanionBrain.SpeechRequest("已附上此刻主人的前台窗口画面。"),
                    image = "data:image/jpeg;base64," + Convert.ToBase64String(memory.ToArray()) };
            }).ToArray();
            await File.WriteAllTextAsync(args[1], JsonSerializer.Serialize(inputs));
            Console.WriteLine("Prepared 8 generated visual fixtures, 768x512; not actual applications.");
            return;
        }
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false });
        int failures = 0;
        bool vision = args.Contains("--vision"), lowInfoOnly = args.Contains("--low-info");
        int repeat = args.Contains("--repeat") ? 2 : 1;
        foreach (var fixture in fixtures.Where(x => !lowInfoOnly || x.Item1 is "desktop" or "newtab" or "homepage"))
        for (int run = 0; run < repeat; run++)
        {
            // Generated test image, not a claim about actual user activity or an app screenshot.
            using var bitmap = new Bitmap(1400, 850);
            using (var g = Graphics.FromImage(bitmap))
            using (var titleFont = new Font("Microsoft YaHei UI", 25))
            using (var bodyFont = new Font("Microsoft YaHei UI", 22))
            {
                g.Clear(Color.White);
                g.DrawString(fixture.Item2, titleFont, Brushes.Black, 30, 15);
                g.DrawString("搜索\n文件\n设置", bodyFont, Brushes.Black, 25, 200);
                for (int i = 0; i < fixture.Item3.Length; i++)
                    g.DrawString(fixture.Item3[i], bodyFont, Brushes.Black, 360, 190 + i * 100);
            }
            using var memory = new MemoryStream();
            bitmap.Save(memory, ImageFormat.Png);
            var image = new CompanionWindowImage("data:image/png;base64," + Convert.ToBase64String(memory.ToArray()), bitmap.Width, bitmap.Height);
            var snapshot = await CompanionOcr.RecognizeAsync(image, "", 0, CancellationToken.None);
            if (!snapshot.Context.Contains(fixture.Item4)) throw new Exception("OCR failed " + fixture.Item1);
            Console.WriteLine($"OCR {fixture.Item1}: words={snapshot.WordCount}, ms={snapshot.RecognizeMilliseconds:F0}");
            if (args.Length == 0) continue;
            var context = snapshot.Context;
            if (fixture.Item1 == "desktop") context = "前台辅助信息：进程explorer，窗口Program Manager。\n" + context;
            var request = vision ? CompanionBrain.SpeechRequest("前台辅助资料：" + fixture.Item2) : CompanionBrain.OcrSpeechRequest(context);
            var persona = vision ? CompanionBrain.Persona : CompanionBrain.OcrPersona;
            // Use exactly the same 768px maximum as the product's vision capture.
            object content = request;
            if (vision)
            {
                using var scaled = new Bitmap(bitmap, 768, 466);
                using var jpeg = new MemoryStream();
                scaled.Save(jpeg, ImageFormat.Jpeg);
                content = new object[] { new { type = "image_url", image_url = new { url = "data:image/jpeg;base64," + Convert.ToBase64String(jpeg.ToArray()) } }, new { type = "text", text = request } };
            }
            var watch = Stopwatch.StartNew();
            using var message = new HttpRequestMessage(HttpMethod.Post, args[0] + "/v1/chat/completions")
            { Content = JsonContent.Create(new
            {
                model = "local", messages = new object[] { new { role = "system", content = persona }, new { role = "user", content } },
                temperature = .65, top_p = .9, max_tokens = 180, stream = true,
                cache_prompt = !args.Contains("no-cache"),
                stream_options = new { include_usage = true },
                chat_template_kwargs = new { enable_thinking = false }
            }) };
            using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            using var reader = new StreamReader(await response.Content.ReadAsStreamAsync());
            var raw = new StringBuilder();
            double? first = null;
            string timings = "";
            while (await reader.ReadLineAsync() is { } line)
            {
                if (!line.StartsWith("data:")) continue;
                var data = line[5..].Trim();
                if (data == "[DONE]") break;
                using var json = JsonDocument.Parse(data);
                var root = json.RootElement;
                if (root.TryGetProperty("timings", out var t)) timings = t.GetRawText();
                if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0) continue;
                if (choices[0].TryGetProperty("delta", out var delta) && delta.TryGetProperty("content", out var deltaContent))
                {
                    var part = deltaContent.GetString();
                    if (!string.IsNullOrEmpty(part)) { first ??= watch.Elapsed.TotalSeconds; raw.Append(part); }
                }
            }
            var text = CompanionBrain.Clean(raw.ToString());
            string[] topics = fixture.Item1 switch
            {
                "development" => new[] { "代码", "开发", "写程序", "编程", "写 OCR", "写OCR" },
                "chat" => new[] { "火锅", "吃饭", "明天" },
                "office" => new[] { "课程", "报告", "总结", "教育实践" },
                "browser" => new[] { "科技", "新闻", "电脑" },
                "video" => new[] { "Photoshop", "绘画", "画画", "光影", "颜色" },
                _ => Array.Empty<string>()
            };
            Console.WriteLine($"MODEL {fixture.Item1}: first={first:F2}s, total={watch.Elapsed.TotalSeconds:F2}s, text={text}, timings={timings}");
            bool lowInfo = fixture.Item1 is "desktop" or "newtab" or "homepage";
            bool inventedTask = lowInfo && System.Text.RegularExpressions.Regex.IsMatch(text,
                "写.{0,8}代码|开发|编程|正在.{0,8}(阅读|看|研究).{0,10}(新闻|论文)|整理|处理.{0,8}文档|撰写|写.{0,8}报告|聊天|火锅|绘画|画画|查资料|找资料|正在.{0,12}(搜索(?!框)|找个|找新|输入)|是不是.{0,15}(新电脑|蓝色大肥鱼|新游戏)|藏.{0,12}回收站");
            if (text.Length < 15 || !text.Contains("主人") || (!lowInfo && !topics.Any(text.Contains)) || inventedTask)
            { failures++; Console.WriteLine("FAIL screen-topic/observer check " + fixture.Item1); }
        }
        Console.WriteLine($"RESULT: {failures} failures in synthetic {(vision ? "vision" : "OCR")} checks; generated content, not real application verification");
        if (failures > 0) Environment.ExitCode = 1;
    }
}
