using System.Net;

namespace Leash.Core;

public enum Protocol
{
    Tcp,
    Udp,
}

public enum TcpState
{
    Unknown = 0,
    Closed = 1,
    Listen = 2,
    SynSent = 3,
    SynReceived = 4,
    Established = 5,
    FinWait1 = 6,
    FinWait2 = 7,
    CloseWait = 8,
    Closing = 9,
    LastAck = 10,
    TimeWait = 11,
    DeleteTcb = 12,
}

public sealed record Connection(Protocol Protocol, IPEndPoint Local, IPEndPoint? Remote, TcpState State, int Pid)
{
    public bool IsOutbound => Remote is { } r && !IPAddress.Any.Equals(r.Address) && !IPAddress.IPv6Any.Equals(r.Address);

    public bool IsLoopback => Remote is { } r && IPAddress.IsLoopback(r.Address);
}
