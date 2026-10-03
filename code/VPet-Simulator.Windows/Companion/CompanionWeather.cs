using System;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VPet_Simulator.Windows;

internal sealed class CompanionWeather(HttpClient web)
{
    private string cachedCode = "", cachedContext = "";
    private DateTime expires;

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

    internal async Task<string> Context(string regionCode, bool refresh, CancellationToken token)
    {
        var region = CompanionRegions.District(regionCode);
        if (region.Latitude == null || region.Longitude == null)
            throw new IOException("所选地区暂缺天气坐标，请选择其他区县。");
        if (!refresh && cachedCode == region.Code && DateTime.UtcNow < expires)
            return cachedContext;
        using var response = await web.GetAsync(ForecastUrl(region), token);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        var current = json.RootElement.GetProperty("current");
        var daily = json.RootElement.GetProperty("daily");
        string Number(JsonElement value) => value.GetDouble().ToString("0.#", CultureInfo.InvariantCulture);
        var context = $"地点：{CompanionRegions.FullName(region)}（区县中心附近的天气估算）。"
            + $"天气数据时间：{current.GetProperty("time").GetString()}（北京时间）。"
            + $"当前{Description(current.GetProperty("weather_code").GetInt32())}，"
            + $"气温{Number(current.GetProperty("temperature_2m"))}℃，体感{Number(current.GetProperty("apparent_temperature"))}℃，"
            + $"相对湿度{Number(current.GetProperty("relative_humidity_2m"))}%，风速{Number(current.GetProperty("wind_speed_10m"))}公里/小时。"
            + $"今天最低{Number(daily.GetProperty("temperature_2m_min")[0])}℃、最高{Number(daily.GetProperty("temperature_2m_max")[0])}℃，"
            + $"最高降水概率{Number(daily.GetProperty("precipitation_probability_max")[0])}%。";
        // Commit the cache only after successful parsing, and always key it by district.
        cachedCode = region.Code;
        cachedContext = context;
        expires = DateTime.UtcNow.AddMinutes(15);
        return context;
    }
}
