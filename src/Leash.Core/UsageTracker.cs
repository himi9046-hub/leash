namespace Leash.Core;

public sealed class AppUsage(string key, string name, string? path)
{
    public const int HistoryLength = 60;

    private readonly Queue<double> _history = new(HistoryLength);

    public string Key { get; } = key;
    public string Name { get; } = name;
    public string? Path { get; } = path;
    public Traffic Total { get; internal set; }
    public Traffic Rate { get; internal set; }
    public List<Connection> Connections { get; } = [];
    public DateTime FirstSeen { get; internal set; }
    public DateTime LastActive { get; internal set; }
    public IEnumerable<double> History => _history;

    public int OutboundCount => Connections.Count(c => c.IsOutbound && !c.IsLoopback);

    internal void Push(double bytesPerSecond)
    {
        if (_history.Count == HistoryLength) _history.Dequeue();
        _history.Enqueue(bytesPerSecond);
    }
}

public sealed class UsageTracker(Func<int, ProcessInfo> resolve, ISet<string> known)
{
    private readonly Dictionary<string, AppUsage> _apps = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<Traffic> _history = new(AppUsage.HistoryLength);

    public event Action<AppUsage>? NewApp;

    public IReadOnlyCollection<AppUsage> Apps => _apps.Values;
    public Traffic Rate { get; private set; }
    public IEnumerable<Traffic> History => _history;

    public void Tick(IReadOnlyList<Connection> connections, IReadOnlyDictionary<int, Traffic> traffic, TimeSpan elapsed, DateTime now)
    {
        var seconds = Math.Max(elapsed.TotalSeconds, 0.001);
        var fresh = new Dictionary<AppUsage, Traffic>();

        foreach (var app in _apps.Values) app.Connections.Clear();

        foreach (var c in connections)
        {
            if (c.Pid == 0) continue;
            App(c.Pid, now).Connections.Add(c);
        }

        foreach (var (pid, bytes) in traffic)
        {
            if (pid == 0 || bytes.Total == 0) continue;
            var app = App(pid, now);
            fresh[app] = fresh.GetValueOrDefault(app) + bytes;
        }

        var total = new Traffic();
        foreach (var app in _apps.Values)
        {
            var bytes = fresh.GetValueOrDefault(app);
            app.Total += bytes;
            app.Rate = new Traffic((long)(bytes.Sent / seconds), (long)(bytes.Received / seconds));
            app.Push(app.Rate.Total);
            total += app.Rate;
            if (bytes.Total > 0) app.LastActive = now;

            if ((bytes.Total > 0 || app.OutboundCount > 0) && known.Add(app.Key)) NewApp?.Invoke(app);
        }

        Rate = total;
        if (_history.Count == AppUsage.HistoryLength) _history.Dequeue();
        _history.Enqueue(total);
    }

    private AppUsage App(int pid, DateTime now)
    {
        var info = resolve(pid);
        if (!_apps.TryGetValue(info.Key, out var app))
        {
            app = new AppUsage(info.Key, info.Name, info.Path) { FirstSeen = now };
            _apps[info.Key] = app;
        }
        return app;
    }
}
