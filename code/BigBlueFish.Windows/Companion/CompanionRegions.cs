using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace BigBlueFish.Windows;

internal sealed record CompanionRegion(string Code, string ParentCode, int Level, string Name, double? Latitude, double? Longitude);

// Country root: China is province → city → district (root "0"); the US is state → county (root "US");
// other countries are first-level division → city (root = ISO code, from GeoNames cities15000).
internal sealed record CompanionCountry(string Code, string Name, string Root, int LeafLevel, string[] Captions);

internal static class CompanionRegions
{
    internal const string DefaultCode = "340104";
    internal static readonly CompanionCountry China = new("CN", "中国 China", "0", 2, ["省 / 自治区 / 直辖市", "市 / 州", "县 / 区"]);
    internal static readonly CompanionCountry UnitedStates = new("US", "美国 United States", "US", 1, ["州", "县 (County)"]);
    // China first, then every other country by English name.
    internal static readonly CompanionCountry[] Countries = LoadCountries();
    private static readonly Dictionary<string, CompanionCountry> ByCountry = Countries.ToDictionary(x => x.Code);
    internal static readonly IReadOnlyList<CompanionRegion> All = Load();
    private static readonly Dictionary<string, CompanionRegion> ByCode = All.ToDictionary(x => x.Code);
    private static readonly ILookup<string, CompanionRegion> ByParent = All.ToLookup(x => x.ParentCode);

    private static CompanionRegion[] Load() =>
        [.. Read<CompanionRegion>("china-regions.json"), .. Read<CompanionRegion>("us-regions.json"), .. Read<CompanionRegion>("world-regions.json")];

    private sealed record WorldCountry(string Code, string Name, string EnglishName);

    private static CompanionCountry[] LoadCountries() =>
        [China, .. Read<WorldCountry>("world-countries.json").Append(new("US", UnitedStates.Name, "United States"))
            .OrderBy(c => c.EnglishName, StringComparer.OrdinalIgnoreCase)
            .Select(c => c.Code == "US" ? UnitedStates : new CompanionCountry(c.Code, c.Name + " " + c.EnglishName, c.Code, 1, ["一级行政区", "城市"]))];

    private static T[] Read<T>(string file)
    {
        using var stream = typeof(CompanionRegions).Assembly.GetManifestResourceStream(
            "BigBlueFish.Windows.assets.regions." + file)
            ?? throw new InvalidOperationException("缺少地区数据：" + file);
        return JsonSerializer.Deserialize<T[]>(stream) ?? throw new InvalidOperationException("地区数据为空：" + file);
    }

    // China codes are numeric; US and world codes start with the ISO country code ("US-06037", "JP.40", "JP-1850147").
    internal static CompanionCountry Country(CompanionRegion region) =>
        char.IsAsciiDigit(region.Code[0]) ? China : ByCountry[region.Code[..2]];

    internal static bool IsUs(CompanionRegion region) => Country(region) == UnitedStates;

    internal static CompanionRegion District(string? code) =>
        code != null && ByCode.TryGetValue(code, out var region) && region.Level == Country(region).LeafLevel ? region : ByCode[DefaultCode];

    internal static CompanionRegion Parent(CompanionRegion region) => ByCode[region.ParentCode];
    internal static CompanionRegion[] Children(string code) => ByParent[code].ToArray();

    // Root-to-leaf chain, e.g. [安徽省, 合肥市, 蜀山区], [加利福尼亚州 CA, Los Angeles County] or [Tokyo, Shinjuku].
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
        if (Country(district) != China) names = names.Prepend(Country(district).Name);
        return string.Join(" · ", names.Distinct());
    }
}
