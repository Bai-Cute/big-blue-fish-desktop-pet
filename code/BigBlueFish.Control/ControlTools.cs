using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BigBlueFish.Control;

public static class ControlTools
{
    public const string PrivacyWarning = "开启连接后，所选 Agent 可以读取和修改大肥鱼的设置，获取当前前台窗口或整个屏幕的截图，并接收你通过鱼鱼输入的内容与交互事件。Agent 可能将这些信息发送给它配置的模型服务。请留意隐私安全，仅连接你信任的 Agent。";
    public const string DailySpeechLabel = "把日常关心的吐字交给 Agent";
    public const string BehaviorControlLabel = "把日常关心的触发和日常的动作切换逻辑交给 Agent";
    public const string BehaviorControlTitle = "让 Agent 安排日常关心与动作";
    public const string DailySpeechDescription = "日常关心触发后，由 Agent 编写鱼鱼要说的话。";
    public const string BehaviorControlDescription = "由 Agent 决定何时关心主人、如何切换动作；没有安排时，鱼鱼保持待机。上一个开关决定由 Agent 还是本地模型编写日常吐字。";
    public const string PuppetWarning = "开启后，鱼鱼会暂停自己的日常关心计时和动作切换，保持待机，等待 Agent 安排。如果 Agent 关闭或断开，需关闭此开关，才能恢复鱼鱼自己的日常关心与动作安排。";
    public static string PipeName => "BigBlueFish.Control." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)))).Substring(0, 20);
    public static object Text(object value) => new { content = new[] { new { type = "text", text = JsonSerializer.Serialize(value) } } };
    public static object Error(string message) => new { isError = true, content = new[] { new { type = "text", text = message } } };
    public static object Image(string dataUrl, object metadata) => new { content = new object[] {
        new { type = "text", text = JsonSerializer.Serialize(metadata) },
        new { type = "image", mimeType = dataUrl[5..dataUrl.IndexOf(';')], data = dataUrl[(dataUrl.IndexOf(',') + 1)..] } } };
    public static readonly string[] SettingNames = ["ModelEnabled", "GpuEnabled", "PublicInfo", "ForegroundEnabled", "StopModelOnBattery",
        "HideOnFullscreen", "CheckUpdatesOnStartup", "WeatherRegionCode", "Scale", "BubbleScale", "SpeechMinMinutes",
        "BubbleDurationIndex", "AnimationIntervalSeconds", "BreakfastHour", "LunchHour", "DinnerHour", "Left", "Top", "LastBrushDate", "StartupEnabled", "PuppetMode", "AgentDailySpeech", "AgentRespondToClick", "AgentRespondToDrag"];
    private const string SettingsInput = """
        {"settings":{"type":"object","additionalProperties":false,"description":"按需提供字段；省略的字段保留。字段名区分大小写。AgentEnabled 只读。成功后读取实际值；位置可能因工作区限制调整。","properties":{
          "ModelEnabled":{"type":"boolean","description":"启用内置模型；不限制外部 say。"},
          "GpuEnabled":{"type":"boolean","description":"本地推理使用 GPU；切换会停止当前模型任务。"},
          "PublicInfo":{"type":"boolean","description":"内置天气和科技新闻。"},
          "ForegroundEnabled":{"type":"boolean","description":"内置模型是否读屏；不是 MCP 截图的权限开关。"},
          "StopModelOnBattery":{"type":"boolean","description":"离电时暂停并卸载内置模型；不限制外部文字。"},
          "HideOnFullscreen":{"type":"boolean","description":"外部全屏时隐藏桌宠；不限制 MCP 截图。"},
          "CheckUpdatesOnStartup":{"type":"boolean","description":"启动时检查更新。"},
          "WeatherRegionCode":{"type":"string","pattern":"^[0-9]{6}$","description":"内置地区表中存在的县区代码，如340104；不是城市名、省级或市级代码。"},
          "Scale":{"type":"number","minimum":0.5,"maximum":2,"description":"角色大小倍率；1为100%。"},
          "BubbleScale":{"type":"number","minimum":0.5,"maximum":2,"description":"独立气泡大小倍率；不改变角色大小。"},
          "SpeechMinMinutes":{"type":"integer","enum":[2,3,5,10,15,20],"description":"说话区间档位：2对应2–3分钟，3对应3–5，5对应5–8，10对应10–16，15对应15–23，20对应20–30；不是任意分钟数。"},
          "BubbleDurationIndex":{"type":"integer","minimum":0,"maximum":7,"description":"索引0–7依次为5、10、15、30、60、120、180秒，以及直到下一句。"},
          "AnimationIntervalSeconds":{"type":"integer","minimum":30,"maximum":300,"description":"普通随机动作事件间隔，单位秒，默认40；等当前片段结束再切换。不是帧率、播放速度或说话间隔。关心触发与动作接管暂停该调度。"},
          "BreakfastHour":{"type":"integer","minimum":0,"maximum":23,"description":"本地时间的早餐小时。"},
          "LunchHour":{"type":"integer","minimum":0,"maximum":23,"description":"本地时间的午餐小时。"},
          "DinnerHour":{"type":"integer","minimum":0,"maximum":23,"description":"本地时间的晚餐小时。"},
          "Left":{"type":["number","null"],"minimum":-1000000,"maximum":1000000,"description":"窗口左坐标：Windows逻辑单位，Linux屏幕像素；Linux截取整数。null不重置位置。"},
          "Top":{"type":["number","null"],"minimum":-1000000,"maximum":1000000,"description":"窗口上坐标，与Left相同的平台语义；最终坐标受屏幕工作区约束。"},
          "LastBrushDate":{"type":["string","null"],"pattern":"^[0-9]{4}-[0-9]{2}-[0-9]{2}$","description":"有效本地日期yyyy-MM-dd或null，用于每日刷牙动作去重；通常无需修改。"},
          "StartupEnabled":{"type":"boolean","description":"当前用户的系统开机启动设置。"},
          "PuppetMode":{"type":"boolean","description":"完全外部接管。每次从false改true须等待用户在桌面确认；窗口等待期间桌宠仍可拖动。断开不会自动退出该模式。"},
          "AgentDailySpeech":{"type":"boolean","description":"日常吐字交给Agent；普通模式由鱼鱼计时，关心触发与动作接管由Agent预约唤醒。"},
          "AgentRespondToClick":{"type":"boolean","description":"点击事件是否主动发起Agent轮次，默认false；事件仍可从交互资源读取。"},
          "AgentRespondToDrag":{"type":"boolean","description":"拖动结束是否主动发起Agent轮次，默认false。"}
        }}}
        """;
    public static object[] Definitions => [
        Tool("get_settings", "读取settings与state。AgentEnabled只读，其余可写字段和范围见update_settings的schema。state是运行快照，不可写；modelAvailable仅表示模型文件可用，不等于推理服务已加载就绪。content[0].text为JSON字符串。", "{}"),
        Tool("update_settings", "按字段修改设置并尝试保存；省略字段保留，空对象也会走应用设置流程。先验证整个补丁，字段或用户确认失败时不应用。不会打开或激活设置窗口。Windows按相关字段停止模型或重排计时；Linux每次应用都会停止模型并重排计时。修改前先读状态；超时、取消或IO错误后先读回，勿盲目重试。启用PuppetMode须等待真实用户确认；本工具不能修改AgentEnabled。", SettingsInput, ["settings"]),
        Tool("list_actions", "列出106个动作的精确action名称、pool、基础weight与名义duration_seconds。复制action原文传给动作工具；分类和权重是自主调度元数据，Disabled动作也可外部指定。名义时长不是完成通知。", "{}"),
        Tool("capture_foreground", "按大肥鱼现有前台窗口选择规则截图，返回内存中的 PNG 图像。会把屏幕内容交给 MCP 连接者。", """{"max_dimension":{"type":"integer","minimum":256,"maximum":16384,"default":2560}}"""),
        Tool("capture_screen", "截取整个虚拟桌面，包含所有显示器，返回内存中的 PNG 图像。会把全屏内容交给 MCP 连接者。", """{"max_dimension":{"type":"integer","minimum":256,"maximum":16384,"default":4096}}"""),
        Tool("play_action", "将指定动作加入最多100项的FIFO队列；重复动作不去重。当前片段结束、未隐藏且未拖动时取下一项。调用成功只表示入队，不表示正在播放或完成；正常点击仍可打断，独占控制请用关心触发与动作接管。action必须精确匹配list_actions。", """{"action":{"type":"string","minLength":1,"maxLength":200}}""", ["action"]),
        Tool("interrupt_action", "立即切换到指定动作，清除尚未播放的全部外部动作并取消移动安排。不会自动显示被隐藏的角色。关心触发与动作接管下播完回到固定待机；普通模式恢复自主调度。成功仅表示已发起播放，不是播放完成。", """{"action":{"type":"string","minLength":1,"maxLength":200}}""", ["action"]),
        Tool("say", "立即按约28个组合字符/秒显示传入text原文，替换当前气泡并取消当前本地生成；不调用模型，不改写、朗读或解析Markdown。不能全为空白，运行时最多4000个UTF-16单元。无需本地模型或插电；不自动恢复隐藏角色，不清除say_next待说原文。displayed=true表示进入显示流程，并非用户已看见或吐字完成。", """{"text":{"type":"string","minLength":1,"maxLength":4000}}""", ["text"]),
        Tool("say_next", "保存到下一次正常日常吐字时机，新调用覆盖旧调用。关心触发与动作接管暂停该节奏；此时立即讲话使用say。", """{"text":{"type":"string","minLength":1,"maxLength":4000}}""", ["text"]),
        Tool("request_local_speech", "立即调用鱼鱼真实本地模型读取当前情境并生成日常关心，沿用正常加载与逐字显示流程。适用于Agent决定时机、本地模型编写吐字；需要AgentDailySpeech=false且本地模型可用，隐藏、忙碌或供电暂停时返回错误。无需传入文字。", "{}"),
        Tool("get_operation_guide", "读取大肥鱼操作技能全文与用户当前人设。", "{}"),
        Tool("get_wakeups", "读取此Agent专用对话的关心预约。", "{}"),
        Tool("schedule_wakeup", "预约delay_seconds秒后的日常关心。PuppetMode=true且Agent已连接时可用。AgentDailySpeech=true时到期向Agent发送wakeup及purpose，由Agent编写；false时到期由鱼鱼直接调用本地模型。支持repeat_seconds重复及已有id修改预约。当前轮次可以结束。", """{"purpose":{"type":"string","minLength":1,"maxLength":1000},"delay_seconds":{"type":"integer","minimum":30,"maximum":604800},"repeat_seconds":{"type":["integer","null"],"minimum":30,"maximum":604800},"id":{"type":"string","maxLength":100}}""", ["purpose", "delay_seconds"]),
        Tool("cancel_wakeup", "取消当前Agent对话的一项预约。", """{"id":{"type":"string","minLength":1,"maxLength":100}}""", ["id"])
    ];
    private static object Tool(string name, string description, string properties, string[]? required = null) => new {
        name, description, inputSchema = new { type = "object", properties = JsonSerializer.Deserialize<JsonElement>(properties),
            required = required ?? [], additionalProperties = false } };
    public static string RequiredText(JsonElement args, string name, int maximum = 4000)
    {
        if (!args.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()) || value.GetString()!.Length > maximum)
            throw new ArgumentException($"{name} 必须是 1–{maximum} 字符的文字。");
        return value.GetString()!;
    }
    public static int ImageLimit(JsonElement args, int fallback)
    {
        if (!args.TryGetProperty("max_dimension", out var value)) return fallback;
        if (!value.TryGetInt32(out var size) || size < 256 || size > 16384) throw new ArgumentException("max_dimension 范围为256–16384。");
        return size;
    }
}
