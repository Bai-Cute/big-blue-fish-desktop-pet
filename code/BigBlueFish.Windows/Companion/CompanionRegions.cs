using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace BigBlueFish.Windows;

internal sealed record CompanionRegion(string Code, string ParentCode, int Level, string Name, double? Latitude, double? Longitude);

// Country root: China is province → city → district (root "0"); the US is state → county (root "US").
internal sealed record CompanionCountry(string Code, string Name, string Root, int LeafLevel, string[] Captions);

internal static class CompanionRegions
{
    internal const string DefaultCode = "340104";
    internal static readonly CompanionCountry China = new("CN", "中国", "0", 2, ["省 / 自治区 / 直辖市", "市 / 州", "县 / 区"]);
    internal static readonly CompanionCountry UnitedStates = new("US", "美国", "US", 1, ["州", "县 (County)"]);
    internal static readonly CompanionCountry[] Countries = [China, UnitedStates];
    internal static readonly IReadOnlyList<CompanionRegion> All = Load();
    private static readonly Dictionary<string, CompanionRegion> ByCode = All.ToDictionary(x => x.Code);
    private static readonly ILookup<string, CompanionRegion> ByParent = All.ToLookup(x => x.ParentCode);

    private static CompanionRegion[] Load() =>
        [.. Read("china-regions.json"), .. Read("us-regions.json")];

    private static CompanionRegion[] Read(string file)
    {
        using var stream = typeof(CompanionRegions).Assembly.GetManifestResourceStream(
            "BigBlueFish.Windows.assets.regions." + file)
            ?? throw new InvalidOperationException("缺少地区数据：" + file);
        return JsonSerializer.Deserialize<CompanionRegion[]>(stream) ?? throw new InvalidOperationException("地区数据为空：" + file);
    }

    internal static CompanionCountry Country(CompanionRegion region) =>
        region.Code.StartsWith("US-", StringComparison.Ordinal) ? UnitedStates : China;

    internal static bool IsUs(CompanionRegion region) => Country(region) == UnitedStates;

    internal static CompanionRegion District(string? code) =>
        code != null && ByCode.TryGetValue(code, out var region) && region.Level == Country(region).LeafLevel ? region : ByCode[DefaultCode];

    internal static CompanionRegion Parent(CompanionRegion region) => ByCode[region.ParentCode];
    internal static CompanionRegion[] Children(string code) => ByParent[code].ToArray();

    // Root-to-leaf chain, e.g. [安徽省, 合肥市, 蜀山区] or [加利福尼亚州 CA, Los Angeles County].
    internal static CompanionRegion[] Chain(CompanionRegion leaf)
    {
        var chain = new List<CompanionRegion> { leaf };
        while (ByCode.TryGetValue(chain[^1].ParentCode, out var parent)) chain.Add(parent);
        chain.Reverse();
        return [.. chain];
    }

    internal static string FullName(CompanionRegion district)
    {
        var names = Chain(district).Select(x => x.Name);
        if (IsUs(district)) names = names.Prepend(UnitedStates.Name);
        return string.Join(" · ", names.Distinct());
    }
}
