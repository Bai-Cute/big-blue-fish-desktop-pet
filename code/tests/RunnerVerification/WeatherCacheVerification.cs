using System.IO;
using System.Net.Http;
using System.Net;
using System.Text.Json;
using BigBlueFish.Windows;

internal static class WeatherCacheVerification
{
    private sealed class Feed : HttpMessageHandler
    {
        internal DateTimeOffset Now;
        internal int Calls;
        internal bool Offline, Invalid, Stale;
        internal TaskCompletionSource? Block;
        internal string LastUrl = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Interlocked.Increment(ref Calls);
            LastUrl = request.RequestUri!.ToString();
            if (Block != null) await Block.Task.WaitAsync(token);
            if (Offline) throw new HttpRequestException("offline test");
            var offset = LastUrl.Contains("timezone=auto") ? TimeSpan.FromHours(-7) : TimeSpan.FromHours(8);
            var observed = (Stale ? Now.AddHours(-7) : Now).ToOffset(offset);
            var text = JsonSerializer.Serialize(new { utc_offset_seconds = (int)offset.TotalSeconds, current = new { time = observed.ToString("yyyy-MM-ddTHH:mm"),
                temperature_2m = 21, apparent_temperature = 20, relative_humidity_2m = 65, weather_code = 0, wind_speed_10m = 8 },
                daily = new { temperature_2m_min = new[] { 18 }, temperature_2m_max = new[] { 25 }, precipitation_probability_max = new[] { 10 } } });
            return new(HttpStatusCode.OK) { Content = new StringContent(Invalid ? "broken json" : text) };
        }
    }
    internal static async Task<int> Run(string directory, bool live)
    {
        directory = Path.GetFullPath(directory);
        Directory.CreateDirectory(directory);
        // Every invocation gets its own data file, avoiding stale fixtures between test runs.
        string file = Path.Combine(directory, Guid.NewGuid() + ".json");
        int checks = 0;
        void Check(bool value, string name) { if (!value) throw new Exception(name); checks++; Console.WriteLine("PASS " + name); }
        var now = new DateTimeOffset(2026, 10, 7, 0, 0, 0, TimeSpan.Zero);
        var feed = new Feed { Now = now, Block = new() };
        using var web = new HttpClient(feed);
        using var cache = new CompanionWeather(web, file, () => now);
        var first = cache.RefreshIfDue("350622");
        Check(Enumerable.Range(0, 50).All(_ => ReferenceEquals(first, cache.RefreshIfDue("350622"))), "concurrent timer and manual requests share one network fetch");
        Check(cache.CachedContext("350622") == "", "ordinary speech returns immediately while first weather fetch is blocked");
        feed.Block.SetResult();
        await first;
        string good = cache.CachedContext("350622");
        Check(good.Contains("21℃") && File.Exists(file), "real parser saves valid weather to disk");
        Check(await cache.Context("350622", default) == good && feed.Calls == 1, "manual weather uses warm cache without another network request");
        now = now.AddMinutes(59); feed.Now = now;
        await cache.RefreshIfDue("350622");
        Check(feed.Calls == 1, "no refresh before one hour");
        now = now.AddMinutes(1); feed.Now = now; feed.Block = new();
        var hourly = cache.RefreshIfDue("350622");
        Check(await cache.Context("350622", default) == good && !hourly.IsCompleted, "cached manual broadcast does not wait for hourly refresh");
        feed.Block.SetResult(); await hourly;
        Check(feed.Calls == 2 && cache.CachedContext("350622") != good, "refresh begins at hour boundary and commits fresh observations");
        good = cache.CachedContext("350622");
        var committedJson = File.ReadAllText(file);
        using var restart = new CompanionWeather(web, file, () => now);
        Check(restart.CachedContext("350622") == good, "restart reloads persistent cache");
        await restart.RefreshIfDue("350622");
        Check(feed.Calls == 2, "restart with recent cache does not fetch again");
        Check(restart.CachedContext("340104") == "", "other district never receives selected district weather");
        feed.Block = null; feed.Offline = true;
        now = now.AddHours(1); feed.Now = now;
        await restart.RefreshIfDue("350622");
        Check(restart.CachedContext("350622") == good, "network failure preserves last successful entry");
        int afterFailure = feed.Calls;
        await restart.RefreshIfDue("350622");
        Check(feed.Calls == afterFailure, "failed refresh is hourly throttled rather than retrying every UI tick");
        now = now.AddHours(5);
        Check(restart.CachedContext("350622") == good, "exact six-hour boundary is usable");
        now = now.AddTicks(1);
        Check(restart.CachedContext("350622") == "", "weather older than six hours is excluded");
        try { await restart.Context("350622", default); throw new Exception("expired cache accepted"); }
        catch (IOException) { Check(true, "manual broadcast fails honestly when offline and cache expired"); }
        feed.Offline = false; feed.Now = now; feed.Stale = true;
        await restart.RefreshIfDue("340104");
        Check(restart.CachedContext("340104") == "", "newly downloaded but seven-hour-old observation is rejected");
        feed.Stale = false; feed.Invalid = true;
        now = now.AddHours(1); feed.Now = now;
        await restart.RefreshIfDue("350622");
        Check(File.ReadAllText(file) == committedJson,
            "malformed refresh leaves last valid JSON file intact");
        File.WriteAllText(file + ".tmp", "partial write");
        using var afterInterruptedWrite = new CompanionWeather(web, file, () => now.AddHours(-7));
        Check(afterInterruptedWrite.CachedContext("350622") == good, "interrupted temporary write cannot replace committed cache");
        File.WriteAllText(file, "broken persistent JSON");
        using var corrupt = new CompanionWeather(web, file, () => now);
        Check(corrupt.CachedContext("350622") == "", "corrupt persistent file is ignored without crashing");
        feed.Invalid = false; feed.Block = new();
        var loading = corrupt.Context("350622", new CancellationToken(true));
        try { await loading; throw new Exception("cancel ignored"); } catch (OperationCanceledException) { }
        feed.Block.SetResult(); await corrupt.RefreshIfDue("350622");
        Check(corrupt.CachedContext("350622").Length > 0, "cancelled manual waiter does not cancel shared background refresh");
        now = now.AddDays(-1);
        Check(corrupt.CachedContext("350622") == "", "clock rollback rejects future-dated cache");
        var la = CompanionRegions.District("US-06037");
        Check(la.Code == "US-06037" && CompanionRegions.FullName(la) == "美国 · 加利福尼亚州 CA · Los Angeles County", "US county is a valid weather leaf");
        Check(CompanionRegions.District("US-06").Code == CompanionRegions.DefaultCode && CompanionRegions.District("340100").Code == CompanionRegions.DefaultCode, "state and city codes fall back to default");
        Check(CompanionRegions.Children("0").All(r => !CompanionRegions.IsUs(r)) && CompanionRegions.Children("US").Length == 52, "China provinces and 52 US states stay separate");
        Check(CompanionRegions.Children("US").All(s => CompanionRegions.Children(s.Code).Length > 0 && CompanionRegions.Children(s.Code).All(c => c.Latitude != null)), "every US state has located counties");
        feed.Now = now;
        await corrupt.RefreshIfDue("US-06037");
        var us = corrupt.CachedContext("US-06037");
        Check(feed.LastUrl.Contains("temperature_unit=fahrenheit") && feed.LastUrl.Contains("wind_speed_unit=mph") && feed.LastUrl.Contains("timezone=auto"), "US request asks for imperial units and local time zone");
        Check(us.Contains("21℉") && us.Contains("英里/小时") && us.Contains("当地时间") && us.Contains("Los Angeles County"), "US weather context uses Fahrenheit, mph and local time");
        await corrupt.RefreshIfDue("350623");
        Check(corrupt.CachedContext("350623").Contains("℃") && feed.LastUrl.Contains("Asia%2FShanghai"), "China weather keeps Celsius and Beijing time");
        if (live)
        {
            string actualFile = Path.Combine(directory, "live-weather.json");
            using var actualWeb = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
            using var actual = new CompanionWeather(actualWeb, actualFile);
            var actualContext = await actual.Context("350622", default);
            Check(actualContext.Contains("℃") && File.Exists(actualFile), "live Open-Meteo fetch is parsed and persisted");
            feed.Offline = true;
            using var offline = new CompanionWeather(web, actualFile);
            Check(await offline.Context("350622", default) == actualContext, "live downloaded weather is reusable after restart with offline HTTP transport");
            Console.WriteLine("LIVE " + actualContext);
            var actualUs = await actual.Context("US-36061", default);
            Check(actualUs.Contains("℉") && actualUs.Contains("New York County"), "live Open-Meteo US fetch is parsed");
            Console.WriteLine("LIVE " + actualUs);
        }
        Console.WriteLine($"RESULT {checks} weather cache checks passed ({CompanionBrain.InputMode})");
        return 0;
    }
}
