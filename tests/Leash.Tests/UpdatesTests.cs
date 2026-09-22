using System.Net;
using Leash.Core;

namespace Leash.Tests;

public class UpdatesTests
{
    private sealed class Stub(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Seen { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Seen = request;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }

    private sealed class Offline : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new HttpRequestException("no network");
    }

    private static string Json(string tag) =>
        $$"""{"tag_name":"{{tag}}","html_url":"https://github.com/himi9046-hub/leash/releases/tag/{{tag}}","draft":false}""";

    [Fact]
    public async Task Finds_a_newer_release()
    {
        var stub = new Stub(HttpStatusCode.OK, Json("v0.2.0"));

        var release = await Updates.NewerThan(new Version(0, 1, 0, 0), new HttpClient(stub));

        Assert.NotNull(release);
        Assert.Equal(new Version(0, 2, 0), release.Version);
        Assert.EndsWith("/v0.2.0", release.Url);
        Assert.Equal("api.github.com", stub.Seen!.RequestUri!.Host);
        Assert.Contains("Leash", stub.Seen.Headers.UserAgent.ToString());
    }

    [Theory]
    [InlineData("v0.1.0")]
    [InlineData("0.0.9")]
    public async Task Ignores_the_same_or_older_release(string tag)
    {
        var release = await Updates.NewerThan(new Version(0, 1, 0, 0), new HttpClient(new Stub(HttpStatusCode.OK, Json(tag))));

        Assert.Null(release);
    }

    [Fact]
    public async Task No_release_yet_is_not_an_update()
    {
        var release = await Updates.NewerThan(new Version(0, 1, 0), new HttpClient(new Stub(HttpStatusCode.NotFound, "{}")));

        Assert.Null(release);
    }

    [Fact]
    public async Task Being_offline_is_not_an_error()
    {
        Assert.Null(await Updates.NewerThan(new Version(0, 1, 0), new HttpClient(new Offline())));
    }

    [Fact]
    public async Task Tags_that_are_not_versions_are_skipped()
    {
        Assert.Null(await Updates.NewerThan(new Version(0, 1, 0), new HttpClient(new Stub(HttpStatusCode.OK, Json("nightly")))));
    }
}
