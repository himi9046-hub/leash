using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Leash.Core;

public sealed record ProcessInfo(int Pid, string Name, string? Path)
{
    public string Key => Path ?? Name;
}

public sealed class ProcessCatalog
{
    private const uint QueryLimitedInformation = 0x1000;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    private readonly Dictionary<int, ProcessInfo> _cache = [];

    public ProcessInfo Get(int pid)
    {
        if (_cache.TryGetValue(pid, out var known)) return known;
        var info = Lookup(pid);
        _cache[pid] = info;
        return info;
    }

    public void Forget(IEnumerable<int> alive)
    {
        var keep = alive.ToHashSet();
        foreach (var pid in _cache.Keys.Where(p => !keep.Contains(p)).ToList())
        {
            _cache.Remove(pid);
        }
    }

    private static ProcessInfo Lookup(int pid)
    {
        if (pid == 0) return new ProcessInfo(0, "System Idle", null);
        if (pid == 4) return new ProcessInfo(4, "System", null);

        var path = ImagePath(pid);
        if (path is not null) return new ProcessInfo(pid, System.IO.Path.GetFileNameWithoutExtension(path), path);

        try
        {
            using var p = Process.GetProcessById(pid);
            return new ProcessInfo(pid, p.ProcessName, null);
        }
        catch (ArgumentException)
        {
            return new ProcessInfo(pid, $"pid {pid}", null);
        }
    }

    private static string? ImagePath(int pid)
    {
        var handle = OpenProcess(QueryLimitedInformation, false, pid);
        if (handle == IntPtr.Zero) return null;
        try
        {
            var size = 1024;
            var sb = new StringBuilder(size);
            return QueryFullProcessImageName(handle, 0, sb, ref size) ? sb.ToString() : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }
}
