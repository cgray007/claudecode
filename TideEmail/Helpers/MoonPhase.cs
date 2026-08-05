namespace TideEmail.Helpers;

/// <summary>Calculates moon phase and illumination percentage from the date alone (no API required).</summary>
internal static class MoonPhase
{
    // Synodic period (new moon to new moon), days
    private const double SynodicPeriod = 29.53058867;

    // Reference new moon: 2000-01-06 18:14 UTC (J2000-era anchor)
    private static readonly DateTime ReferenceNewMoon =
        new(2000, 1, 6, 18, 14, 0, DateTimeKind.Utc);

    /// <summary>Returns the moon phase name and illuminated fraction (0–100) for the given date.</summary>
    internal static (string Name, string Emoji, double Illumination) Calculate(DateOnly date)
    {
        var noon = new DateTime(date.Year, date.Month, date.Day, 12, 0, 0, DateTimeKind.Utc);
        var elapsed = (noon - ReferenceNewMoon).TotalDays % SynodicPeriod;
        if (elapsed < 0) elapsed += SynodicPeriod;

        var fraction     = elapsed / SynodicPeriod;          // 0..1 within one lunation
        var illumination = (1 - Math.Cos(2 * Math.PI * fraction)) / 2 * 100;

        var (name, emoji) = fraction switch
        {
            < 0.0625 or >= 0.9375 => ("New Moon",        "🌑"),
            < 0.1875              => ("Waxing Crescent",  "🌒"),
            < 0.3125              => ("First Quarter",    "🌓"),
            < 0.4375              => ("Waxing Gibbous",   "🌔"),
            < 0.5625              => ("Full Moon",        "🌕"),
            < 0.6875              => ("Waning Gibbous",   "🌖"),
            < 0.8125              => ("Third Quarter",    "🌗"),
            _                    => ("Waning Crescent",  "🌘"),
        };

        return (name, emoji, illumination);
    }
}
