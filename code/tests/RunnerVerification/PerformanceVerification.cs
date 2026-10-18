using System.Text.Json;
using System.IO;
using BigBlueFish.Windows;

internal static class PerformanceVerification
{
    internal static int Export(string destination)
    {
        var systems = new HashSet<string>();
        for (int i = 0; i < 100 && systems.Count < 4; i++)
            systems.Add((string)CompanionBrain.BuildMessages(CompanionBrain.OcrPersona, "")[0].content);
        if (systems.Count != 4) throw new Exception("Expected the four production reaction variants");
        string context = "现在是2026年10月18日 15:20。\n前台应用：代码编辑器。\n窗口标题：蓝色大肥鱼项目。\n正文：正在排查本地模型在RTX3050笔记本上的加载问题，并验证不同电脑上的首字等待时间。\n";
        var paragraphs = Enumerable.Range(0, 24).Select(i => $"第{i+1}条工作记录：检查模型加载、显存预算、任务取消、健康检测和流式气泡显示，保持桌宠日常动作与主人设置。\n");
        var data = new { normal = systems.ToArray(), weather = CompanionBrain.WeatherPersona, news = CompanionBrain.NewsPersona,
            requests = new[] { CompanionBrain.OcrSpeechRequest(context), CompanionBrain.OcrSpeechRequest(context+string.Concat(paragraphs.Take(8))), CompanionBrain.OcrSpeechRequest(context+string.Concat(paragraphs)) },
            syntheticContexts = true, unchangedProductionPrompts = true };
        File.WriteAllText(destination, JsonSerializer.Serialize(data));
        Console.WriteLine("Exported unchanged production prompts for actual-runner latency measurements.");
        return 0;
    }
}
