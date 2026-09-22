using System.Collections.Concurrent;
using System.Net;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;

namespace Leash.Core;

public sealed class DnsNames : IDisposable
{
    private const int QueryCompleted = 3008;

    private readonly ConcurrentDictionary<IPAddress, string> _names = new();
    private TraceEventSession? _session;

    public void Start()
    {
        if (_session is not null || !TrafficMonitor.CanRun) return;

        var session = new TraceEventSession("Leash-Dns") { StopOnDispose = true };
        session.EnableProvider("Microsoft-Windows-DNS-Client");
        new RegisteredTraceEventParser(session.Source).All += OnEvent;

        _session = session;
        new Thread(() => session.Source.Process()) { IsBackground = true, Name = "leash-dns" }.Start();
    }

    public string? NameOf(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        return _names.TryGetValue(address, out var name) ? name : null;
    }

    internal void Remember(string query, string results)
    {
        if (string.IsNullOrWhiteSpace(query)) return;
        foreach (var part in results.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var text = part.StartsWith("::ffff:", StringComparison.OrdinalIgnoreCase) ? part[7..] : part;
            if (IPAddress.TryParse(text, out var ip)) _names[ip] = query.TrimEnd('.');
        }
    }

    private void OnEvent(TraceEvent e)
    {
        if ((int)e.ID != QueryCompleted) return;
        if (e.PayloadByName("QueryName") is string query && e.PayloadByName("QueryResults") is string results)
        {
            Remember(query, results);
        }
    }

    public void Dispose()
    {
        _session?.Dispose();
        _session = null;
    }
}
