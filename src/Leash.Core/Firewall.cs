using System.Security.Cryptography;
using System.Text;

namespace Leash.Core;

public sealed class Firewall
{
    private const string Group = "Leash";
    private const int ActionBlock = 0;
    private const int DirectionIn = 1;
    private const int DirectionOut = 2;
    private const int AllProfiles = 0x7FFFFFFF;

    private readonly HashSet<string> _blocked = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlySet<string> Blocked => _blocked;

    public void Refresh()
    {
        _blocked.Clear();
        foreach (var rule in Rules())
        {
            string? group = rule.Grouping;
            string? app = rule.ApplicationName;
            if (group == Group && app is not null) _blocked.Add(app);
        }
    }

    public bool IsBlocked(string path) => _blocked.Contains(path);

    public void Block(string path)
    {
        var policy = Policy();
        foreach (var direction in new[] { DirectionOut, DirectionIn })
        {
            var name = RuleName(path, direction);
            Remove(policy, name);

            dynamic rule = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FWRule", true)!)!;
            rule.Name = name;
            rule.Description = $"Blocked by Leash: {path}";
            rule.Grouping = Group;
            rule.ApplicationName = path;
            rule.Direction = direction;
            rule.Action = ActionBlock;
            rule.Profiles = AllProfiles;
            rule.Enabled = true;
            policy.Rules.Add(rule);
        }
        _blocked.Add(path);
    }

    public void Unblock(string path)
    {
        var policy = Policy();
        Remove(policy, RuleName(path, DirectionOut));
        Remove(policy, RuleName(path, DirectionIn));
        _blocked.Remove(path);
    }

    internal static string RuleName(string path, int direction)
    {
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(path.ToLowerInvariant())))[..10];
        var dir = direction == DirectionOut ? "out" : "in";
        return $"Leash {dir} {System.IO.Path.GetFileName(path)} {hash}";
    }

    private static dynamic Policy() => Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FwPolicy2", true)!)!;

    private static IEnumerable<dynamic> Rules()
    {
        foreach (var rule in Policy().Rules) yield return rule;
    }

    private static void Remove(dynamic policy, string name)
    {
        foreach (var rule in policy.Rules)
        {
            if (rule.Name == name)
            {
                policy.Rules.Remove(name);
                return;
            }
        }
    }
}
