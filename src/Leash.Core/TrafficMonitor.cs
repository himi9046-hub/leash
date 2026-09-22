using System.Collections.Concurrent;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;

namespace Leash.Core;

public readonly record struct Traffic(long Sent, long Received)
{
    public long Total => Sent + Received;

    public static Traffic operator +(Traffic a, Traffic b) => new(a.Sent + b.Sent, a.Received + b.Received);
}

public sealed class TrafficMonitor : IDisposable
{
    private readonly ConcurrentDictionary<int, long[]> _bytes = new();
    private TraceEventSession? _session;

    public bool Running => _session is not null;

    public static bool CanRun => TraceEventSession.IsElevated() == true;

    public void Start()
    {
        if (_session is not null || !CanRun) return;

        var session = new TraceEventSession("Leash-Network") { StopOnDispose = true };
        session.EnableKernelProvider(KernelTraceEventParser.Keywords.NetworkTCPIP);

        var kernel = session.Source.Kernel;
        kernel.TcpIpSend += e => Add(e.ProcessID, e.size, 0);
        kernel.TcpIpRecv += e => Add(e.ProcessID, 0, e.size);
        kernel.TcpIpSendIPV6 += e => Add(e.ProcessID, e.size, 0);
        kernel.TcpIpRecvIPV6 += e => Add(e.ProcessID, 0, e.size);
        kernel.UdpIpSend += e => Add(e.ProcessID, e.size, 0);
        kernel.UdpIpRecv += e => Add(e.ProcessID, 0, e.size);
        kernel.UdpIpSendIPV6 += e => Add(e.ProcessID, e.size, 0);
        kernel.UdpIpRecvIPV6 += e => Add(e.ProcessID, 0, e.size);

        _session = session;
        new Thread(() => session.Source.Process()) { IsBackground = true, Name = "leash-etw" }.Start();
    }

    public Dictionary<int, Traffic> Drain()
    {
        var result = new Dictionary<int, Traffic>(_bytes.Count);
        foreach (var pid in _bytes.Keys)
        {
            if (!_bytes.TryRemove(pid, out var counts)) continue;
            result[pid] = new Traffic(Interlocked.Read(ref counts[0]), Interlocked.Read(ref counts[1]));
        }
        return result;
    }

    internal void Add(int pid, long sent, long received)
    {
        if (pid < 0) return;
        var counts = _bytes.GetOrAdd(pid, _ => new long[2]);
        Interlocked.Add(ref counts[0], sent);
        Interlocked.Add(ref counts[1], received);
    }

    public void Dispose()
    {
        _session?.Dispose();
        _session = null;
    }
}
