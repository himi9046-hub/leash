using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Leash;

public static class Icons
{
    private static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource? For(string? path)
    {
        if (path is null) return null;
        if (Cache.TryGetValue(path, out var cached)) return cached;

        ImageSource? image = null;
        if (File.Exists(path))
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (icon is not null)
            {
                image = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(32, 32));
                image.Freeze();
            }
        }
        Cache[path] = image;
        return image;
    }

    public static System.Drawing.Icon App()
    {
        var stream = Application.GetResourceStream(new Uri("pack://application:,,,/leash.ico"))!.Stream;
        return new System.Drawing.Icon(stream, System.Windows.Forms.SystemInformation.SmallIconSize);
    }

    public static PointCollection Line(IEnumerable<double> values, double width, double height, double max)
    {
        var list = values.ToList();
        var points = new PointCollection(list.Count);
        if (list.Count < 2) return points;

        var step = width / (Core.AppUsage.HistoryLength - 1);
        var x = width - step * (list.Count - 1);
        foreach (var v in list)
        {
            points.Add(new Point(x, height - Math.Min(v / max, 1) * (height - 1)));
            x += step;
        }
        points.Freeze();
        return points;
    }
}
