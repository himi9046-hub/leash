using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace Leash.Core;

public static class ConnectionTable
{
    private const int TcpTableOwnerPidAll = 5;
    private const int UdpTableOwnerPid = 1;
    private const uint ErrorInsufficientBuffer = 122;

    [DllImport("iphlpapi.dll")]
    private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, int family, int tableClass, int reserved);

    [DllImport("iphlpapi.dll")]
    private static extern uint GetExtendedUdpTable(IntPtr table, ref int size, bool order, int family, int tableClass, int reserved);

    [StructLayout(LayoutKind.Sequential)]
    private struct TcpRow
    {
        public uint State;
        public uint LocalAddr;
        public uint LocalPort;
        public uint RemoteAddr;
        public uint RemotePort;
        public int Pid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct TcpRow6
    {
        public fixed byte LocalAddr[16];
        public uint LocalScope;
        public uint LocalPort;
        public fixed byte RemoteAddr[16];
        public uint RemoteScope;
        public uint RemotePort;
        public uint State;
        public int Pid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UdpRow
    {
        public uint LocalAddr;
        public uint LocalPort;
        public int Pid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct UdpRow6
    {
        public fixed byte LocalAddr[16];
        public uint LocalScope;
        public uint LocalPort;
        public int Pid;
    }

    public static List<Connection> Snapshot()
    {
        var list = new List<Connection>(512);
        Read<TcpRow>(true, AddressFamily.InterNetwork, TcpTableOwnerPidAll, r => list.Add(new Connection(
            Protocol.Tcp,
            new IPEndPoint(new IPAddress(r.LocalAddr), Port(r.LocalPort)),
            new IPEndPoint(new IPAddress(r.RemoteAddr), Port(r.RemotePort)),
            (TcpState)r.State,
            r.Pid)));
        Read<TcpRow6>(true, AddressFamily.InterNetworkV6, TcpTableOwnerPidAll, r => list.Add(ToConnection(r)));
        Read<UdpRow>(false, AddressFamily.InterNetwork, UdpTableOwnerPid, r => list.Add(new Connection(
            Protocol.Udp,
            new IPEndPoint(new IPAddress(r.LocalAddr), Port(r.LocalPort)),
            null,
            TcpState.Unknown,
            r.Pid)));
        Read<UdpRow6>(false, AddressFamily.InterNetworkV6, UdpTableOwnerPid, r => list.Add(ToConnection(r)));
        return list;
    }

    internal static int Port(uint raw) => (int)(((raw & 0xFF) << 8) | ((raw >> 8) & 0xFF));

    private static unsafe Connection ToConnection(TcpRow6 r)
    {
        var local = new IPAddress(new ReadOnlySpan<byte>(r.LocalAddr, 16), r.LocalScope);
        var remote = new IPAddress(new ReadOnlySpan<byte>(r.RemoteAddr, 16), r.RemoteScope);
        return new Connection(Protocol.Tcp, new IPEndPoint(local, Port(r.LocalPort)), new IPEndPoint(remote, Port(r.RemotePort)), (TcpState)r.State, r.Pid);
    }

    private static unsafe Connection ToConnection(UdpRow6 r)
    {
        var local = new IPAddress(new ReadOnlySpan<byte>(r.LocalAddr, 16), r.LocalScope);
        return new Connection(Protocol.Udp, new IPEndPoint(local, Port(r.LocalPort)), null, TcpState.Unknown, r.Pid);
    }

    private static unsafe void Read<T>(bool tcp, AddressFamily family, int tableClass, Action<T> add) where T : unmanaged
    {
        var size = 0;
        var buffer = IntPtr.Zero;
        try
        {
            for (var attempt = 0; attempt < 4; attempt++)
            {
                var result = tcp
                    ? GetExtendedTcpTable(buffer, ref size, false, (int)family, tableClass, 0)
                    : GetExtendedUdpTable(buffer, ref size, false, (int)family, tableClass, 0);
                if (result == 0) break;
                if (result != ErrorInsufficientBuffer) return;
                Marshal.FreeHGlobal(buffer);
                buffer = Marshal.AllocHGlobal(size);
            }
            if (buffer == IntPtr.Zero) return;

            var count = Marshal.ReadInt32(buffer);
            var rows = (T*)(buffer + 4);
            for (var i = 0; i < count; i++)
            {
                add(rows[i]);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
