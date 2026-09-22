using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Leash.Core;

namespace Leash.Tests;

public class CoreTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1024, "1.0 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(10 * 1024, "10 KB")]
    [InlineData(5L * 1024 * 1024 * 1024, "5.0 GB")]
    [InlineData(3L * 1024 * 1024 * 1024 * 1024 * 1024, "3072 TB")]
    public void Formats_byte_counts(long bytes, string expected) => Assert.Equal(expected, Bytes.Format(bytes));

    [Fact]
    public void Idle_rate_is_blank() => Assert.Equal("", Bytes.Rate(0.4));

    [Fact]
    public void Ports_come_in_network_byte_order() => Assert.Equal(443, ConnectionTable.Port(0xBB01));

    [Fact]
    public void Sees_our_own_listening_socket()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var mine = ConnectionTable.Snapshot().Where(c => c.Protocol == Protocol.Tcp && c.Local.Port == port).ToList();

        var row = Assert.Single(mine);
        Assert.Equal(TcpState.Listen, row.State);
        Assert.Equal(Environment.ProcessId, row.Pid);
    }

    [Fact]
    public void Sees_an_established_connection_from_both_ends()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var client = new TcpClient();
        client.Connect(IPAddress.Loopback, port);
        using var server = listener.AcceptTcpClient();

        var rows = ConnectionTable.Snapshot().Where(c => c.State == TcpState.Established && (c.Local.Port == port || c.Remote?.Port == port)).ToList();

        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.True(r.IsLoopback));
    }

    [Fact]
    public void Resolves_the_current_process()
    {
        var info = new ProcessCatalog().Get(Environment.ProcessId);

        Assert.Equal(Process.GetCurrentProcess().MainModule!.FileName, info.Path, ignoreCase: true);
        Assert.Equal(info.Path, info.Key);
    }

    [Fact]
    public void Dns_answers_map_addresses_to_names()
    {
        var dns = new DnsNames();
        dns.Remember("api.github.com.", "140.82.121.6;::ffff:140.82.121.5;2606:50c0:8000::64;type:  5 github.com;");

        Assert.Equal("api.github.com", dns.NameOf(IPAddress.Parse("140.82.121.6")));
        Assert.Equal("api.github.com", dns.NameOf(IPAddress.Parse("140.82.121.5")));
        Assert.Equal("api.github.com", dns.NameOf(IPAddress.Parse("::ffff:140.82.121.6")));
        Assert.Equal("api.github.com", dns.NameOf(IPAddress.Parse("2606:50c0:8000::64")));
        Assert.Null(dns.NameOf(IPAddress.Parse("1.1.1.1")));
    }

    [Fact]
    public void Firewall_rule_names_are_stable_and_case_insensitive()
    {
        var a = Firewall.RuleName(@"C:\Apps\Spotify.exe", 2);
        var b = Firewall.RuleName(@"c:\apps\spotify.EXE", 2);

        Assert.Equal(a, b.Replace("spotify.EXE", "Spotify.exe"));
        Assert.StartsWith("Leash out Spotify.exe ", a);
        Assert.NotEqual(a, Firewall.RuleName(@"C:\Apps\Spotify.exe", 1));
    }

    [Fact]
    public void Known_apps_survive_a_restart()
    {
        var file = Path.Combine(Path.GetTempPath(), $"leash-{Guid.NewGuid():N}", "known.json");
        try
        {
            var first = new KnownApps(file);
            Assert.True(first.FirstRun);
            first.Set.Add(@"C:\Apps\chrome.exe");
            first.Save();

            var second = new KnownApps(file);
            Assert.False(second.FirstRun);
            Assert.Contains(@"c:\apps\CHROME.EXE", second.Set);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(file)!, true);
        }
    }

    [Fact]
    public void Traffic_counter_drains_and_resets()
    {
        using var monitor = new TrafficMonitor();
        monitor.Add(7, 100, 0);
        monitor.Add(7, 0, 50);
        monitor.Add(-1, 999, 999);

        Assert.Equal(new Traffic(100, 50), monitor.Drain()[7]);
        Assert.Empty(monitor.Drain());
    }
}
