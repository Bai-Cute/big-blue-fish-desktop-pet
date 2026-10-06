using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace VPet_Simulator.Windows;

internal sealed class CompanionAnimationRule
{
    public string Id { get; set; } = "";
    public string File { get; set; } = "";
    public string Pool { get; set; } = "Event";
    public double Weight { get; set; } = 100;
    public bool NightOnly { get; set; }
    public string? SeasonOnly { get; set; }
    public double? SummerWeight { get; set; }
    public double? WinterWeight { get; set; }
    public double? MorningWeight { get; set; }
    public string? Meal { get; set; }
    public double MealBoost { get; set; }
    public string? Festival { get; set; }
    public int RadiusDays { get; set; }
    public string? FestivalMode { get; set; }
    public double FestivalWeight { get; set; }
    public string? WorkKind { get; set; }
    public double WorkWeight { get; set; }
    public string? MoveKind { get; set; }
    public int HourParity { get; set; }
}

internal sealed record CompanionAnimationContext(DateTime LocalTime,
    bool WorkRecognitionEnabled = false, bool IsWorking = false, bool IsCoding = false,
    int BreakfastHour = 8, int LunchHour = 12, int DinnerHour = 18);

// Pure policy: calendar and pool selection never capture the screen or invoke a model.
internal sealed class CompanionAnimationPolicy
{
    internal IReadOnlyList<CompanionAnimationRule> Rules { get; }
    private readonly Dictionary<int, Dictionary<string, List<DateTime>>> calendars = new();
    internal CompanionAnimationPolicy(IReadOnlyList<CompanionAnimationRule> rules) => Rules = rules;
    internal static CompanionAnimationPolicy Load(string directory)
    {
        var rules = JsonSerializer.Deserialize<List<CompanionAnimationRule>>(File.ReadAllText(Path.Combine(directory, "animations.json")))
            ?? throw new InvalidDataException("动画规则为空");
        if (rules.Count != 106 || rules.Select(r => r.Id).Distinct().Count() != rules.Count)
            throw new InvalidDataException("动画规则不完整或有重复");
        foreach (var rule in rules)
            if (Path.GetFileName(rule.File) != rule.File || !File.Exists(Path.Combine(directory, rule.File)))
                throw new FileNotFoundException("动画素材缺失", rule.File);
        return new(rules);
    }
    internal CompanionAnimationRule Find(string id) => Rules.First(r => r.Id == id);
    internal static string Season(DateTime date) => date.Month switch
    { >= 3 and <= 5 => "Spring", >= 6 and <= 8 => "Summer", >= 9 and <= 11 => "Autumn", _ => "Winter" };
    private static bool NearHour(DateTime date, int hour)
    {
        var minutes = Math.Abs(date.TimeOfDay.TotalMinutes - hour * 60);
        return Math.Min(minutes, 1440 - minutes) <= 60;
    }
    private static bool AtMeal(CompanionAnimationRule rule, CompanionAnimationContext context) => rule.Meal switch
    {
        "Breakfast" => NearHour(context.LocalTime, context.BreakfastHour),
        "Lunch" => NearHour(context.LocalTime, context.LunchHour),
        "Dinner" => NearHour(context.LocalTime, context.DinnerHour),
        "Any" => NearHour(context.LocalTime, context.BreakfastHour) || NearHour(context.LocalTime, context.LunchHour) || NearHour(context.LocalTime, context.DinnerHour),
        _ => false
    };
    internal double Weight(CompanionAnimationRule rule, CompanionAnimationContext context)
    {
        var time = context.LocalTime;
        var season = Season(time);
        if (rule.Pool == "Disabled" || rule.NightOnly && time.Hour is >= 8 and < 23 ||
            rule.SeasonOnly != null && rule.SeasonOnly != season ||
            rule.MoveKind != null && (time.Minute < 5 || time.Minute >= 55 || time.Hour % 2 != rule.HourParity)) return 0;
        var weight = rule.Weight;
        if (season == "Summer" && rule.SummerWeight.HasValue) weight = rule.SummerWeight.Value;
        if (season == "Winter" && rule.WinterWeight.HasValue) weight = rule.WinterWeight.Value;
        if (time.Hour is >= 8 and < 11 && rule.MorningWeight.HasValue) weight = rule.MorningWeight.Value;
        bool mealEligible = true;
        if (rule.Festival != null)
        {
            var distance = FestivalDistance(time.Date, rule.Festival);
            if (rule.FestivalMode == "Triangle")
                weight += (rule.FestivalWeight - weight) * Math.Max(0, 1 - distance / rule.RadiusDays);
            else
            {
                mealEligible = distance <= rule.RadiusDays;
                if (mealEligible) weight = rule.FestivalWeight;
            }
        }
        if (mealEligible && AtMeal(rule, context)) weight += rule.MealBoost;
        if (context.WorkRecognitionEnabled &&
            (rule.WorkKind == "Working" && context.IsWorking || rule.WorkKind == "Coding" && context.IsCoding))
            weight = rule.WorkWeight;
        return Math.Max(0, weight);
    }
    internal CompanionAnimationRule Choose(string pool, CompanionAnimationContext context, Random random)
    {
        var weighted = Rules.Where(r => r.Pool == pool).Select(r => (Rule: r, Weight: Weight(r, context))).Where(r => r.Weight > 0).ToArray();
        if (weighted.Length == 0) throw new InvalidOperationException("动画池为空：" + pool);
        var point = random.NextDouble() * weighted.Sum(r => r.Weight);
        foreach (var entry in weighted) { point -= entry.Weight; if (point < 0) return entry.Rule; }
        return weighted[^1].Rule;
    }
    internal double FestivalDistance(DateTime date, string festival)
    {
        var dates = Enumerable.Range(Math.Max(1, date.Year - 1), Math.Min(9999, date.Year + 1) - Math.Max(1, date.Year - 1) + 1)
            .SelectMany(y => Calendar(y).TryGetValue(festival, out var found) ? found : new List<DateTime>());
        return dates.Select(d => Math.Abs((date.Date - d).TotalDays)).DefaultIfEmpty(double.PositiveInfinity).Min();
    }
    private Dictionary<string, List<DateTime>> Calendar(int year)
    {
        if (calendars.TryGetValue(year, out var cached)) return cached;
        var result = new Dictionary<string, List<DateTime>>();
        void Add(string name, DateTime date) { if (!result.TryGetValue(name, out var list)) result[name] = list = new(); list.Add(date); }
        Add("Christmas", new(year, 12, 25)); Add("Halloween", new(year, 10, 31));
        Add("ValentineOrMay20", new(year, 2, 14)); Add("ValentineOrMay20", new(year, 5, 20));
        var lunar = new ChineseLunisolarCalendar();
        for (var date = new DateTime(year, 1, 1); date.Year == year;)
        {
            if (date >= lunar.MinSupportedDateTime && date <= lunar.MaxSupportedDateTime)
            {
                int month = lunar.GetMonth(date), day = lunar.GetDayOfMonth(date), leap = lunar.GetLeapMonth(lunar.GetYear(date));
                if (leap == 0 || month != leap)
                {
                    if (leap != 0 && month > leap) month--;
                    string? name = (month, day) switch { (1, 1) => "SpringFestival", (1, 15) => "Lantern", (5, 5) => "DragonBoat", (7, 7) => "Qixi", (8, 15) => "MidAutumn", (12, 8) => "Laba", _ => null };
                    if (name != null) Add(name, date);
                }
            }
            if (date == DateTime.MaxValue.Date) break;
            date = date.AddDays(1);
        }
        if (calendars.Count > 9) calendars.Clear();
        return calendars[year] = result;
    }
}
