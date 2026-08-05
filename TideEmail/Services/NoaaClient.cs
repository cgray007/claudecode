using System.Globalization;
using System.Text.Json;
using TideEmail.Helpers;
using TideEmail.Models;

namespace TideEmail.Services;

/// <summary>
/// NOAA Tides and Currents API.
/// Tides are averaged between two bracketing stations to estimate conditions at 136th Street, OC:
///   StationNorth — Ocean City (Inlet), MD (8570283)
///   StationSouth — Lewes, DE             (8558690)
/// Water temperature comes from StationSouth.
/// </summary>
internal static class NoaaClient
{
    private const string StationNorth = "8570283"; // Ocean City (Inlet), MD
    private const string StationSouth = "8558690"; // Lewes, DE

    internal static async Task<List<TidePrediction>> FetchTides(DateOnly today)
    {
        var dateStr = today.ToString("yyyyMMdd", Formatting.Inv);

        // Fetch both stations in parallel
        var taskA = FetchRawTides(StationNorth, dateStr);
        var taskB = FetchRawTides(StationSouth, dateStr);
        await Task.WhenAll(taskA, taskB);

        return AverageTides(taskA.Result, taskB.Result);
    }

    private static async Task<List<TidePrediction>> FetchRawTides(string station, string dateStr)
    {
        var url = "https://api.tidesandcurrents.noaa.gov/api/prod/datagetter"
                  + $"?begin_date={dateStr}&end_date={dateStr}&station={station}"
                  + "&product=predictions&datum=MLLW&time_zone=lst_ldt&interval=hilo"
                  + "&units=english&application=tide_email&format=json";
        using var doc = JsonDocument.Parse(await SharedHttp.Client.GetStringAsync(url));
        if (doc.RootElement.TryGetProperty("error", out var error))
            throw new InvalidOperationException($"NOAA API error (station {station}): {error.GetProperty("message").GetString()}");

        var tides = new List<TidePrediction>();
        foreach (var entry in doc.RootElement.GetProperty("predictions").EnumerateArray())
        {
            tides.Add(new TidePrediction(
                DateTime.ParseExact(entry.GetProperty("t").GetString()!, "yyyy-MM-dd HH:mm", Formatting.Inv),
                entry.GetProperty("type").GetString()!,
                double.Parse(entry.GetProperty("v").GetString()!, Formatting.Inv)));
        }
        return tides;
    }

    /// <summary>
    /// Pairs nth-high with nth-high and nth-low with nth-low across both station lists,
    /// then averages each pair's time and height.
    /// </summary>
    private static List<TidePrediction> AverageTides(List<TidePrediction> a, List<TidePrediction> b)
    {
        var highsA = a.Where(t => t.Type == "H").OrderBy(t => t.Time).ToList();
        var lowsA  = a.Where(t => t.Type == "L").OrderBy(t => t.Time).ToList();
        var highsB = b.Where(t => t.Type == "H").OrderBy(t => t.Time).ToList();
        var lowsB  = b.Where(t => t.Type == "L").OrderBy(t => t.Time).ToList();

        var result = new List<TidePrediction>();
        for (var i = 0; i < Math.Min(highsA.Count, highsB.Count); i++)
            result.Add(new TidePrediction(MidTime(highsA[i].Time, highsB[i].Time), "H",
                                          (highsA[i].Height + highsB[i].Height) / 2.0));
        for (var i = 0; i < Math.Min(lowsA.Count, lowsB.Count); i++)
            result.Add(new TidePrediction(MidTime(lowsA[i].Time, lowsB[i].Time), "L",
                                          (lowsA[i].Height + lowsB[i].Height) / 2.0));

        return [.. result.OrderBy(t => t.Time)];
    }

    private static DateTime MidTime(DateTime a, DateTime b) =>
        new((a.Ticks + b.Ticks) / 2, a.Kind);

    /// <summary>
    /// Returns (avg temp °F, reading count) from the previous 24 hours.
    /// Fetches yesterday + today, filters to readings within the last 24 hours,
    /// then averages the 20 lowest readings (all of them when fewer than 20).
    /// Returns (null, 0) on failure or no data.
    /// </summary>
    internal static async Task<(double? AvgTempF, int ReadingCount)> FetchWaterTemp(DateOnly today)
    {
        var yesterday = today.AddDays(-1);
        var url = "https://api.tidesandcurrents.noaa.gov/api/prod/datagetter"
                  + $"?begin_date={yesterday.ToString("yyyyMMdd", Formatting.Inv)}&end_date={today.ToString("yyyyMMdd", Formatting.Inv)}"
                  + $"&station={StationSouth}&product=water_temperature&time_zone=lst_ldt"
                  + "&units=english&format=json";
        try
        {
            using var doc = JsonDocument.Parse(await SharedHttp.Client.GetStringAsync(url));
            if (!doc.RootElement.TryGetProperty("data", out var readings))
                return (null, 0);

            var cutoff = DateTime.Now - TimeSpan.FromHours(24);
            var values = new List<double>();
            foreach (var r in readings.EnumerateArray())
            {
                if (DateTime.TryParseExact(r.GetProperty("t").GetString(), "yyyy-MM-dd HH:mm", Formatting.Inv,
                        DateTimeStyles.None, out var t)
                    && t >= cutoff
                    && double.TryParse(r.GetProperty("v").GetString(), NumberStyles.Float, Formatting.Inv, out var v))
                {
                    values.Add(v);
                }
            }
            if (values.Count > 0)
            {
                var lowest = values.OrderBy(v => v).Take(20).ToList();
                return (lowest.Average(), lowest.Count);
            }
        }
        catch
        {
            // Water temp is optional — continue without it on any failure.
        }
        return (null, 0);
    }
}
