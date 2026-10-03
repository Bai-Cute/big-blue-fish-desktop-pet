using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace VPet_Simulator.Windows;

internal sealed record CompanionRegion(string Code, string ParentCode, int Level, string Name, double? Latitude, double? Longitude);

internal static class CompanionRegions
{
    internal const string DefaultCode = "340104";
    internal static readonly IReadOnlyList<CompanionRegion> All = Load();
    private static readonly Dictionary<string, CompanionRegion> ByCode = All.ToDictionary(x => x.Code);

    private static CompanionRegion[] Load()
    {
        using var stream = typeof(CompanionRegions).Assembly.GetManifestResourceStream(
            "VPet_Simulator.Windows.assets.regions.china-regions.json")
            ?? throw new InvalidOperationException("缺少省市区数据");
        return JsonSerializer.Deserialize<CompanionRegion[]>(stream) ?? throw new InvalidOperationException("省市区数据为空");
    }

    internal static CompanionRegion District(string? code) =>
        code != null && ByCode.TryGetValue(code, out var region) && region.Level == 2 ? region : ByCode[DefaultCode];

    internal static CompanionRegion Parent(CompanionRegion region) => ByCode[region.ParentCode];
    internal static CompanionRegion[] Children(string code) => All.Where(x => x.ParentCode == code).ToArray();
    internal static string FullName(CompanionRegion district) => string.Join(" · ",
        new[] { Parent(Parent(district)).Name, Parent(district).Name, district.Name }.Distinct());
}
