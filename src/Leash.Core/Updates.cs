using System.Text.Json;

namespace Leash.Core;

public sealed record Release(Version Version, string Url);

public static class Updates
{
    private const string Latest = "https://api.github.com/repos/himi9046-hub/leash/releases/latest";

    public static async Task<Release?> NewerThan(Version current, HttpClient http, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Latest);
        request.Headers.UserAgent.ParseAdd("Leash");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        try
        {
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return null;
            var release = Parse(await response.Content.ReadAsStringAsync(ct));
            return release is not null && release.Version > Trim(current) ? release : null;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
    }

    internal static Release? Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version)) return null;
        return new Release(Trim(version), root.GetProperty("html_url").GetString() ?? "");
    }

    private static Version Trim(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));
}
