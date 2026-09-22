using System.Globalization;

namespace Leash.Core;

public static class Bytes
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    public static string Format(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        var format = value < 10 ? "0.0" : "0";
        return value.ToString(format, CultureInfo.InvariantCulture) + " " + Units[unit];
    }

    public static string Rate(double bytesPerSecond) => bytesPerSecond < 1 ? "" : Format((long)bytesPerSecond) + "/s";
}
