using System;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using BigBlueFish.Windows;

internal static partial class Program
{
    static void VerifyAiPower(MainWindow window)
    {
        var native = typeof(App).Assembly.GetType("BigBlueFish.Windows.CompanionNative")!;
        var power = native.GetField("TestPowerOverride", Access)!;
        var preferences = Read(window, "preferences");
        var timer = Read(window, "companionTimer"); Invoke(timer, "Stop");
        try
        {
            Set(preferences, "StopModelOnBattery", true);
            Set(preferences, "AgentEnabled", true); Set(preferences, "AgentDailySpeech", true);
            power.SetValue(null, false); Invoke(window, "ApplyCompanionPowerPolicy");
            Check(Property<bool>(preferences, "AiPausedOnBattery"), "battery policy pauses both AI writers");
            Check(!Property<bool>(window, "McpInputAvailable"), "battery policy blocks double-click dialogue");
            foreach (string method in new[] { "SpeakCompanionTest", "SpeakCompanionWeather" })
            {
                var task = (Task)Invoke(window, method)!;
                Wait(() => task.IsCompleted, 3, "manual battery request finishes without Agent work"); task.GetAwaiter().GetResult();
                var speech = Read(window, "speech");
                Check(Property<string>(speech, "FullText").Contains("拔电时暂停鱼鱼的AI功能"), "manual Agent test/weather explains the configured AI pause");
                Check(!Property<bool>(Read(window, "brain"), "Busy"), "manual Agent request does not start local inference");
            }
            using var args = JsonDocument.Parse("{\"text\":\"stale Agent reply\"}");
            var stale = (Task<object>)Invoke(window, "ExecuteMcp", "say", args.RootElement, CancellationToken.None)!;
            Wait(() => stale.IsCompleted, 3, "stale tool request returns during power pause");
            Check(stale.IsFaulted && stale.Exception!.GetBaseException() is InvalidOperationException, "late Agent reply cannot bypass battery pause");
            Check(Property<bool>(preferences, "AgentEnabled") && Property<bool>(preferences, "AgentDailySpeech"), "power pause preserves user Agent choices");
            power.SetValue(null, true); Invoke(window, "ApplyCompanionPowerPolicy");
            Check(!Property<bool>(preferences, "AiPausedOnBattery") && Property<bool>(window, "McpInputAvailable"), "plugging back in restores dialogue availability");
            power.SetValue(null, false); Set(preferences, "StopModelOnBattery", false); Invoke(window, "ApplyCompanionPowerPolicy");
            Check(!Property<bool>(preferences, "AiPausedOnBattery") && Property<bool>(window, "McpInputAvailable"), "disabling the power switch permits AI on battery");
        }
        finally { power.SetValue(null, null); Set(preferences, "AgentEnabled", false); Set(preferences, "AgentDailySpeech", false); }
        Console.WriteLine($"AI power: {checks} actual WPF/power-gate checks passed.");
    }
}
