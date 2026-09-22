using System.Net;
using Leash.Core;

namespace Leash.Tests;

public class UsageTrackerTests
{
    private static readonly DateTime T0 = new(2026, 9, 22, 12, 0, 0);

    private static readonly Dictionary<int, ProcessInfo> Processes = new()
    {
        [10] = new(10, "chrome", @"C:\Apps\chrome.exe"),
        [11] = new(11, "chrome", @"C:\Apps\chrome.exe"),
        [20] = new(20, "spotify", @"C:\Apps\Spotify.exe"),
        [30] = new(30, "svchost", null),
    };

    private static Connection Out(int pid, string ip = "93.184.216.34") =>
        new(Protocol.Tcp, new IPEndPoint(IPAddress.Parse("192.168.1.5"), 50000), new IPEndPoint(IPAddress.Parse(ip), 443), TcpState.Established, pid);

    private static Connection Listening(int pid) =>
        new(Protocol.Tcp, new IPEndPoint(IPAddress.Any, 8080), new IPEndPoint(IPAddress.Any, 0), TcpState.Listen, pid);

    private static UsageTracker Tracker(ISet<string>? known = null) => new(pid => Processes[pid], known ?? new HashSet<string>());

    [Fact]
    public void Groups_processes_of_the_same_executable()
    {
        var tracker = Tracker();
        tracker.Tick([Out(10), Out(11)], new Dictionary<int, Traffic> { [10] = new(100, 1000), [11] = new(50, 500) }, TimeSpan.FromSeconds(1), T0);

        var chrome = Assert.Single(tracker.Apps);
        Assert.Equal(@"C:\Apps\chrome.exe", chrome.Key);
        Assert.Equal(new Traffic(150, 1500), chrome.Total);
        Assert.Equal(2, chrome.Connections.Count);
    }

    [Fact]
    public void Rate_is_bytes_per_second_of_the_last_tick()
    {
        var tracker = Tracker();
        tracker.Tick([], new Dictionary<int, Traffic> { [20] = new(2000, 8000) }, TimeSpan.FromSeconds(2), T0);
        tracker.Tick([], new Dictionary<int, Traffic>(), TimeSpan.FromSeconds(1), T0.AddSeconds(1));

        var spotify = Assert.Single(tracker.Apps);
        Assert.Equal(new Traffic(0, 0), spotify.Rate);
        Assert.Equal(new Traffic(2000, 8000), spotify.Total);
        Assert.Equal([5000d, 0d], spotify.History);
        Assert.Equal(5000, tracker.History.First().Total);
    }

    [Fact]
    public void Announces_an_app_once_when_it_first_goes_online()
    {
        var known = new HashSet<string>();
        var tracker = Tracker(known);
        var announced = new List<string>();
        tracker.NewApp += a => announced.Add(a.Name);

        tracker.Tick([Listening(20)], new Dictionary<int, Traffic>(), TimeSpan.FromSeconds(1), T0);
        Assert.Empty(announced);

        tracker.Tick([Listening(20), Out(10)], new Dictionary<int, Traffic>(), TimeSpan.FromSeconds(1), T0);
        tracker.Tick([Out(10)], new Dictionary<int, Traffic> { [10] = new(1, 1) }, TimeSpan.FromSeconds(1), T0);

        Assert.Equal(["chrome"], announced);
        Assert.Contains(@"C:\Apps\chrome.exe", known);
    }

    [Fact]
    public void Already_known_apps_are_not_announced()
    {
        var tracker = Tracker(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"c:\apps\CHROME.exe" });
        var announced = 0;
        tracker.NewApp += _ => announced++;

        tracker.Tick([Out(10)], new Dictionary<int, Traffic>(), TimeSpan.FromSeconds(1), T0);

        Assert.Equal(0, announced);
    }

    [Fact]
    public void Loopback_connections_do_not_count_as_going_online()
    {
        var tracker = Tracker();
        var announced = 0;
        tracker.NewApp += _ => announced++;

        tracker.Tick([Out(20, "127.0.0.1")], new Dictionary<int, Traffic>(), TimeSpan.FromSeconds(1), T0);

        Assert.Equal(0, announced);
        Assert.Equal(0, tracker.Apps.Single().OutboundCount);
    }

    [Fact]
    public void Processes_without_a_path_are_keyed_by_name_and_idle_is_ignored()
    {
        var tracker = Tracker();
        tracker.Tick([Out(30), Out(0)], new Dictionary<int, Traffic> { [0] = new(5, 5) }, TimeSpan.FromSeconds(1), T0);

        Assert.Equal(["svchost"], tracker.Apps.Select(a => a.Key));
    }

    [Fact]
    public void History_keeps_the_last_minute()
    {
        var tracker = Tracker();
        for (var i = 0; i < 90; i++)
        {
            tracker.Tick([], new Dictionary<int, Traffic> { [20] = new(i, 0) }, TimeSpan.FromSeconds(1), T0.AddSeconds(i));
        }

        var history = tracker.Apps.Single().History.ToList();
        Assert.Equal(AppUsage.HistoryLength, history.Count);
        Assert.Equal(30, history[0]);
        Assert.Equal(89, history[^1]);
    }
}
