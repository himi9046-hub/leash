using System.Windows.Media;
using Leash.Core;

namespace Leash;

public sealed class AppRow(string key, string name, string? path) : Observable
{
    private double _rate;
    private string _down = "";
    private string _up = "";
    private string _total = "";
    private int _connections;
    private bool _blocked;
    private PointCollection _spark = [];

    public string Key { get; } = key;
    public string Name { get; } = name;
    public string? Path { get; } = path;
    public string Where => Path is null ? "system process" : System.IO.Path.GetDirectoryName(Path) ?? "";
    public ImageSource? Icon => Icons.For(Path);
    public bool CanBlock => Path is not null;

    public double RateValue { get => _rate; private set => Set(ref _rate, value); }
    public long TotalValue { get; private set; }
    public string Down { get => _down; private set => Set(ref _down, value); }
    public string Up { get => _up; private set => Set(ref _up, value); }
    public string Total { get => _total; private set => Set(ref _total, value); }
    public int Connections { get => _connections; private set => Set(ref _connections, value); }
    public bool IsBlocked { get => _blocked; set => Set(ref _blocked, value); }
    public PointCollection Spark { get => _spark; private set => Set(ref _spark, value); }

    public void Update(AppUsage usage)
    {
        RateValue = usage.Rate.Total;
        TotalValue = usage.Total.Total;
        Down = Bytes.Rate(usage.Rate.Received);
        Up = Bytes.Rate(usage.Rate.Sent);
        Total = usage.Total.Total == 0 ? "" : Bytes.Format(usage.Total.Total);
        Connections = usage.OutboundCount;
        var peak = Math.Max(usage.History.DefaultIfEmpty().Max(), 1024);
        Spark = Icons.Line(usage.History, 64, 18, peak);
    }
}

public sealed record ConnectionRow(string Protocol, string Host, string Port, string State, string Local);
