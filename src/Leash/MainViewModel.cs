using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Data;
using System.Windows.Media;
using Leash.Core;

namespace Leash;

public sealed class MainViewModel : Observable, IDisposable
{
    private readonly ProcessCatalog _processes = new();
    private readonly TrafficMonitor _traffic = new();
    private readonly DnsNames _dns = new();
    private readonly Firewall _firewall = new();
    private readonly KnownApps _known = new(KnownApps.DefaultFile);
    private readonly UsageTracker _tracker;
    private readonly Dictionary<string, AppRow> _rows = new(StringComparer.OrdinalIgnoreCase);
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private bool _quiet;

    private AppRow? _selected;
    private string _filter = "";
    private string _down = "";
    private string _up = "";
    private PointCollection _downLine = [];
    private PointCollection _upLine = [];
    private string? _error;

    public MainViewModel()
    {
        _tracker = new UsageTracker(_processes.Get, _known.Set);
        _tracker.NewApp += app =>
        {
            if (_quiet) return;
            _known.Save();
            NewApp?.Invoke(app.Name, app.Key);
        };
        _quiet = _known.FirstRun;

        Apps = CollectionViewSource.GetDefaultView(Rows);
        Apps.SortDescriptions.Add(new SortDescription(nameof(AppRow.RateValue), ListSortDirection.Descending));
        Apps.SortDescriptions.Add(new SortDescription(nameof(AppRow.TotalValue), ListSortDirection.Descending));
        Apps.SortDescriptions.Add(new SortDescription(nameof(AppRow.Name), ListSortDirection.Ascending));
        if (Apps is ICollectionViewLiveShaping live && live.CanChangeLiveSorting)
        {
            live.LiveSortingProperties.Add(nameof(AppRow.RateValue));
            live.LiveSortingProperties.Add(nameof(AppRow.TotalValue));
            live.IsLiveSorting = true;
        }
        Apps.Filter = o => o is AppRow r && (_filter.Length == 0 || r.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase) || (r.Path?.Contains(_filter, StringComparison.OrdinalIgnoreCase) ?? false));

        ToggleBlock = new Command(o => Toggle((AppRow)o!), o => Elevated && o is AppRow { CanBlock: true });
        RevealSelected = new Command(_ => Reveal(), _ => Selected?.Path is not null);

        if (Elevated)
        {
            _traffic.Start();
            _dns.Start();
        }
        _firewall.Refresh();
    }

    public event Action<string, string>? NewApp;

    public bool Elevated { get; } = TrafficMonitor.CanRun;
    public ObservableCollection<AppRow> Rows { get; } = [];
    public ICollectionView Apps { get; }
    public ObservableCollection<ConnectionRow> Connections { get; } = [];
    public Command ToggleBlock { get; }
    public Command RevealSelected { get; }

    public AppRow? Selected
    {
        get => _selected;
        set
        {
            if (Set(ref _selected, value)) FillConnections();
        }
    }

    public string Filter
    {
        get => _filter;
        set
        {
            if (Set(ref _filter, value.Trim())) Apps.Refresh();
        }
    }

    public string Down { get => _down; private set => Set(ref _down, value); }
    public string Up { get => _up; private set => Set(ref _up, value); }
    public PointCollection DownLine { get => _downLine; private set => Set(ref _downLine, value); }
    public PointCollection UpLine { get => _upLine; private set => Set(ref _upLine, value); }
    public string? Error { get => _error; set => Set(ref _error, value); }

    public void Select(string key)
    {
        if (_rows.TryGetValue(key, out var row)) Selected = row;
    }

    public void Tick()
    {
        var elapsed = _clock.Elapsed;
        _clock.Restart();

        var connections = ConnectionTable.Snapshot();
        _tracker.Tick(connections, _traffic.Drain(), elapsed, DateTime.Now);
        _processes.Forget(connections.Select(c => c.Pid));

        if (_quiet)
        {
            _quiet = false;
            _known.Save();
        }

        foreach (var usage in _tracker.Apps)
        {
            Row(usage.Key, usage.Name, usage.Path).Update(usage);
        }
        foreach (var path in _firewall.Blocked)
        {
            Row(path, System.IO.Path.GetFileNameWithoutExtension(path), path);
        }

        var rate = _tracker.Rate;
        Down = Bytes.Rate(rate.Received) is { Length: > 0 } d ? d : "0 B/s";
        Up = Bytes.Rate(rate.Sent) is { Length: > 0 } u ? u : "0 B/s";
        var history = _tracker.History.ToList();
        var peak = Math.Max(history.Select(t => (double)Math.Max(t.Sent, t.Received)).DefaultIfEmpty().Max(), 16 * 1024);
        DownLine = Icons.Line(history.Select(t => (double)t.Received), 220, 34, peak);
        UpLine = Icons.Line(history.Select(t => (double)t.Sent), 220, 34, peak);

        FillConnections();
    }

    private AppRow Row(string key, string name, string? path)
    {
        if (_rows.TryGetValue(key, out var row)) return row;
        row = new AppRow(key, name, path) { IsBlocked = path is not null && _firewall.IsBlocked(path) };
        _rows[key] = row;
        Rows.Add(row);
        return row;
    }

    private void FillConnections()
    {
        Connections.Clear();
        var usage = _tracker.Apps.FirstOrDefault(a => a.Key == _selected?.Key);
        if (usage is null) return;

        var rows = usage.Connections
            .Where(c => c.IsOutbound || c.State == TcpState.Listen || c.Protocol == Protocol.Udp)
            .OrderBy(c => c.IsOutbound ? 0 : 1)
            .ThenBy(c => c.Remote?.Address.ToString())
            .Select(c => new ConnectionRow(
                c.Protocol == Protocol.Tcp ? "TCP" : "UDP",
                c.IsOutbound ? _dns.NameOf(c.Remote!.Address) ?? c.Remote.Address.ToString() : c.State == TcpState.Listen ? "listening" : "open",
                c.IsOutbound ? c.Remote!.Port.ToString() : "",
                c.Protocol == Protocol.Tcp ? Describe(c.State) : "",
                c.Local.Port.ToString()));
        foreach (var row in rows) Connections.Add(row);
    }

    private static string Describe(TcpState state) => state switch
    {
        TcpState.Established => "open",
        TcpState.Listen => "",
        TcpState.SynSent => "connecting",
        TcpState.TimeWait or TcpState.CloseWait or TcpState.FinWait1 or TcpState.FinWait2 or TcpState.LastAck or TcpState.Closing => "closing",
        _ => state.ToString().ToLowerInvariant(),
    };

    private void Toggle(AppRow row)
    {
        try
        {
            if (row.IsBlocked) _firewall.Unblock(row.Path!);
            else _firewall.Block(row.Path!);
            row.IsBlocked = !row.IsBlocked;
            Error = null;
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            Error = $"Could not change the firewall rule: {e.Message}";
        }
    }

    private void Reveal()
    {
        if (Selected?.Path is { } path) Process.Start("explorer.exe", $"/select,\"{path}\"");
    }

    public void Dispose()
    {
        _traffic.Dispose();
        _dns.Dispose();
        _known.Save();
    }
}
