using System;
using System.Globalization;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VPet_Simulator.Windows;

internal sealed class CompanionWeather : IDisposable
{
    private readonly HttpClient web;
    private readonly string? cachePath;
    private readonly Func<DateTimeOffset> clock;
    private readonly object sync = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly Dictionary<string, Entry> entries = new();
    private readonly Dictionary<string, DateTimeOffset> attempts = new();
    private readonly Dictionary<string, Task> pending = new();
    internal sealed record Entry(string Code, DateTimeOffset FetchedAt, DateTimeOffset ObservedAt, string Context);

    internal CompanionWeather(HttpClient web, string? cachePath = null, Func<DateTimeOffset>? clock = null)
    {
        this.web = web;
        this.cachePath = cachePath == null ? null : Path.GetFullPath(cachePath);
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
        if (this.cachePath != null)
        {
            try
            {
                foreach (var entry in JsonSerializer.Deserialize<Entry[]>(File.ReadAllText(this.cachePath)) ?? [])
                    if (entry != null && !string.IsNullOrWhiteSpace(entry.Code) && !string.IsNullOrWhiteSpace(entry.Context))
                        entries[entry.Code] = entry;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { }
        }
    }

    private bool Fresh(Entry entry, DateTimeOffset now) =>
        now >= entry.FetchedAt && now >= entry.ObservedAt
        && now - entry.FetchedAt <= TimeSpan.FromHours(6)
        && now - entry.ObservedAt <= TimeSpan.FromHours(6);

    // Called by the normal UI timer; no HTTP or disk work runs on the UI thread.
    internal Task RefreshIfDue(string regionCode)
    {
        var region = CompanionRegions.District(regionCode);
        lock (sync)
        {
            if (lifetime.IsCancellationRequested) return Task.CompletedTask;
            if (pending.TryGetValue(region.Code, out var active) && !active.IsCompleted) return active;
            var now = clock();
            var last = attempts.TryGetValue(region.Code, out var attempt) ? attempt
                : entries.TryGetValue(region.Code, out var cached) ? cached.FetchedAt : DateTimeOffset.MinValue;
            if (now >= last && now - last < TimeSpan.FromHours(1)) return Task.CompletedTask;
            attempts[region.Code] = now;
            return pending[region.Code] = Task.Run(() => Fetch(region));
        }
    }

    internal string CachedContext(string regionCode)
    {
        var code = CompanionRegions.District(regionCode).Code;
        lock (sync)
            return entries.TryGetValue(code, out var entry) && Fresh(entry, clock()) ? entry.Context : "";
    }

    internal async Task<string> Context(string regionCode, CancellationToken token)
    {
        var refresh = RefreshIfDue(regionCode);
        var cached = CachedContext(regionCode);
        if (cached.Length > 0) return cached;
        await refresh.WaitAsync(token);
        cached = CachedContext(regionCode);
        if (cached.Length == 0) throw new IOException("暂时没有六小时内的天气资料，请稍后重试。");
        return cached;
    }

    public void Dispose() { lock (sync) lifetime.Cancel(); }


    internal static string Description(int code) => code switch
    {
        0 => "晴朗", 1 => "大部晴朗", 2 => "局部多云", 3 => "阴天",
        45 or 48 => "有雾", 51 or 53 or 55 => "毛毛雨", 56 or 57 => "冻毛毛雨",
        61 => "小雨", 63 => "中雨", 65 => "大雨", 66 or 67 => "冻雨",
        71 => "小雪", 73 => "中雪", 75 => "大雪", 77 => "雪粒",
        80 => "小阵雨", 81 => "阵雨", 82 => "强阵雨", 85 or 86 => "阵雪",
        95 => "雷雨", 96 or 99 => "雷雨伴冰雹", _ => "天气状况未提供"
    };

    internal static string ForecastUrl(CompanionRegion region) =>
        "https://api.open-meteo.com/v1/forecast?latitude=" + region.Latitude!.Value.ToString(CultureInfo.InvariantCulture)
        + "&longitude=" + region.Longitude!.Value.ToString(CultureInfo.InvariantCulture)
        + "&current=temperature_2m,apparent_temperature,relative_humidity_2m,weather_code,wind_speed_10m"
        + "&daily=temperature_2m_max,temperature_2m_min,precipitation_probability_max"
        + "&forecast_days=1&timezone=Asia%2FShanghai";

    private async Task Fetch(CompanionRegion region)
    {
        try
        {
            if (region.Latitude == null || region.Longitude == null) return;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            deadline.CancelAfter(TimeSpan.FromSeconds(12));
            var token = deadline.Token;
            using var response = await web.GetAsync(ForecastUrl(region), token);
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            var current = json.RootElement.GetProperty("current");
            var daily = json.RootElement.GetProperty("daily");
            var observed = new DateTimeOffset(DateTime.Parse(current.GetProperty("time").GetString()!,
                CultureInfo.InvariantCulture, DateTimeStyles.None), TimeSpan.FromHours(8));
            string Number(JsonElement value) => value.GetDouble().ToString("0.#", CultureInfo.InvariantCulture);
            var context = $"地点：{CompanionRegions.FullName(region)}（区县中心附近的天气估算）。"
                + $"天气资料记录时间：{current.GetProperty("time").GetString()}（北京时间）。"
                + $"记录时{Description(current.GetProperty("weather_code").GetInt32())}，"
                + $"气温{Number(current.GetProperty("temperature_2m"))}℃，体感{Number(current.GetProperty("apparent_temperature"))}℃，"
                + $"相对湿度{Number(current.GetProperty("relative_humidity_2m"))}%，风速{Number(current.GetProperty("wind_speed_10m"))}公里/小时。"
                + $"资料日期{observed:yyyy-MM-dd}最低{Number(daily.GetProperty("temperature_2m_min")[0])}℃、最高{Number(daily.GetProperty("temperature_2m_max")[0])}℃，"
                + $"最高降水概率{Number(daily.GetProperty("precipitation_probability_max")[0])}%。";
            var entry = new Entry(region.Code, clock(), observed, context);
            lock (sync)
            {
                if (lifetime.IsCancellationRequested || !Fresh(entry, clock())) return;
                entries[region.Code] = entry;
                if (cachePath != null)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
                    var temporary = cachePath + ".tmp";
                    File.WriteAllText(temporary, JsonSerializer.Serialize(new List<Entry>(entries.Values)));
                    File.Move(temporary, cachePath, overwrite: true);
                }
            }
        }
        // A failed hourly refresh retains the last good entry, never extending its lifetime.
        catch (Exception e) when (e is HttpRequestException or IOException or UnauthorizedAccessException
            or OperationCanceledException or JsonException or InvalidOperationException or FormatException
            or KeyNotFoundException or ArgumentException or ObjectDisposedException) { }
    }
}
