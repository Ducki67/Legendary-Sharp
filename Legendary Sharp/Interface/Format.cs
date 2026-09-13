using System.Globalization;

namespace Legendary_Sharp.Interface;

internal static class Format
{
    private static readonly string[] SizeUnits = ["B", "KiB", "MiB", "GiB", "TiB"];

    public static string Bytes(long value)
    {
        if (value < 0) return "-";
        double size = value;
        var unit = 0;
        while (size >= 1024d && unit < SizeUnits.Length - 1)
        {
            size /= 1024d;
            unit++;
        }

        var precision = unit == 0 ? 0 : size >= 100d ? 1 : 2;
        return string.Create(CultureInfo.InvariantCulture, $"{Math.Round(size, precision)} {SizeUnits[unit]}");
    }

    public static string Rate(double bytesPerSecond) =>
        bytesPerSecond <= 0d ? "0 B/s" : $"{Bytes((long)bytesPerSecond)}/s";

    public static string Duration(TimeSpan value)
    {
        if (value < TimeSpan.Zero || value.TotalDays >= 7) return "--:--:--";
        return value.TotalHours >= 1d
            ? string.Create(CultureInfo.InvariantCulture, $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{value.Minutes:00}:{value.Seconds:00}");
    }

    public static string Count(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    public static string Percent(double fraction) =>
        string.Create(CultureInfo.InvariantCulture, $"{Math.Clamp(fraction, 0d, 1d) * 100d:0.0}%");

    public static string Truncate(string value, int width)
    {
        if (width <= 0) return string.Empty;
        if (value.Length <= width) return value;
        return width <= 1 ? value[..width] : string.Concat(value.AsSpan(0, width - 1), "\u2026");
    }

    public static string TruncateStart(string value, int width)
    {
        if (width <= 0) return string.Empty;
        if (value.Length <= width) return value;
        return width <= 1 ? value[^width..] : string.Concat("\u2026", value.AsSpan(value.Length - width + 1));
    }
}
