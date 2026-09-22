using System.Text.Json;

namespace Leash.Core;

public sealed class KnownApps
{
    private readonly string _file;

    public KnownApps(string file)
    {
        _file = file;
        FirstRun = !File.Exists(file);
        if (!FirstRun)
        {
            var saved = JsonSerializer.Deserialize<string[]>(File.ReadAllText(file)) ?? [];
            Set.UnionWith(saved);
        }
    }

    public bool FirstRun { get; }

    public HashSet<string> Set { get; } = new(StringComparer.OrdinalIgnoreCase);

    public static string DefaultFile =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Leash", "known.json");

    public void Save()
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_file)!);
        File.WriteAllText(_file, JsonSerializer.Serialize(Set.Order(StringComparer.OrdinalIgnoreCase)));
    }
}
