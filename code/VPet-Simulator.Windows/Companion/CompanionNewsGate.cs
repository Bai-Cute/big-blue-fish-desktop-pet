using System;
using System.IO;
using System.Globalization;
using System.Text.RegularExpressions;

namespace VPet_Simulator.Windows;
// Only the last reserved calendar day is persisted; no conversation content.
internal sealed class CompanionNewsGate
{
    internal static bool IsClearTitle(string title) => title.Length >= 12 && title.Length <= 160 && !Regex.IsMatch(title, @"某|据传|网传|疑似|曝光|爆料|或将|即将|重磅|震撼|不一样|大幅提升|悬念|[？?]") && Regex.IsMatch(title, @"发布|推出|开源|上线|宣布|支持|更新|正式|获批|发现");
    private DateTime nextEligible;
    private int ordinarySpeeches;
    private DateTime? lastNewsDay;
    private readonly string? statePath;
    internal CompanionNewsGate(DateTime now, string? statePath = null)
    {
        nextEligible = now.AddHours(2);
        this.statePath = statePath;
        if (statePath != null)
            try
            {
                lastNewsDay = DateTime.ParseExact(File.ReadAllText(statePath).Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
            catch
            {
            }
    }

    internal void RecordOrdinarySpeech() => ordinarySpeeches = Math.Min(12, ordinarySpeeches + 1);
    internal bool TryReserve(DateTime now, double roll)
    {
        if (now < nextEligible || lastNewsDay == now.Date || ordinarySpeeches < 12 || roll >= .02)
            return false;
        if (statePath != null)
        {
            try
            {
                File.WriteAllText(statePath + ".tmp", now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                File.Move(statePath + ".tmp", statePath, true);
            }
            catch
            {
                return false;
            }
        }

        lastNewsDay = now.Date;
        nextEligible = now.AddHours(2);
        ordinarySpeeches = 0;
        return true;
    }
}
